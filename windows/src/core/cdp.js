import { connect } from 'net';
import CDP from 'chrome-remote-interface';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

export function probePort(port) {
  return new Promise((resolve) => {
    const s = connect({ port, host: '127.0.0.1' });
    let done = false;
    const finish = (v) => {
      if (done) return;
      done = true;
      s.destroy();
      resolve(v);
    };
    s.once('connect', () => finish(true));
    s.once('error', () => finish(false));
    s.setTimeout(700, () => finish(false));
  });
}

export async function waitForEndpoint(port, timeoutMs = 25000) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    try {
      await CDP.Version({ port });
      return true;
    } catch {
      await sleep(400);
    }
  }
  return false;
}

export async function startInjector(port, source, onAttach) {
  const clients = new Map();
  const seen = new Set();
  let stopped = false;
  let scanning = false;

  async function armPreload(client, sessionId) {
    try {
      if (sessionId) await client.send('Page.enable', {}, sessionId);
      else await client.Page.enable();
    } catch {}
    try {
      if (sessionId) {
        await client.send('Page.addScriptToEvaluateOnNewDocument', { source }, sessionId);
      } else {
        await client.Page.addScriptToEvaluateOnNewDocument({ source });
      }
    } catch {}
  }

  async function evalNow(client, sessionId) {
    try {
      if (sessionId) {
        await client.send('Runtime.enable', {}, sessionId);
        await client.send('Runtime.evaluate', { expression: source }, sessionId);
      } else {
        await client.Runtime.evaluate({ expression: source });
      }
    } catch {}
  }

  function burstEval(client, sessionId) {
    const delays = [400, 1200, 2800, 5000];
    for (const ms of delays) {
      setTimeout(() => {
        if (stopped) return;
        evalNow(client, sessionId).catch(() => {});
      }, ms);
    }
  }

  async function agentAlive(client, sessionId) {
    try {
      const opts = { expression: '!!(window.__intelbyteAgent && window.__intelbyteAgent.data)', returnByValue: true };
      const res = sessionId
        ? await client.send('Runtime.evaluate', opts, sessionId)
        : await client.Runtime.evaluate(opts);
      return !!(res && res.result && res.result.value);
    } catch {
      return false;
    }
  }

  async function ensureLivePage(client, target) {
    const url = ((target && target.url) || '').split('#')[0];
    if (!/^https?:\/\//.test(url)) return;
    await sleep(700);
    if (stopped || (await agentAlive(client))) return;
    await evalNow(client);
    await sleep(500);
    if (stopped || (await agentAlive(client))) return;
    try {
      await client.Page.reload({ ignoreCache: false });
    } catch {}
  }

  async function waitForLoad(client) {
    try {
      const res = await client.Runtime.evaluate({ expression: 'document.readyState', returnByValue: true });
      if (res && res.result && res.result.value === 'loading') {
        await new Promise((resolve) => {
          const done = () => resolve();
          client.once('Page.loadEventFired', done);
          setTimeout(done, 10000);
        });
      }
    } catch {}
  }

  async function wireChildFrames(client) {
    client.on('Target.attachedToTarget', async (params) => {
      const sid = params && params.sessionId;
      if (!sid || stopped) return;
      await armPreload(client, sid);
      await evalNow(client, sid);
      burstEval(client, sid);
      await client
        .send(
          'Target.setAutoAttach',
          { autoAttach: true, waitForDebuggerOnStart: false, flatten: true },
          sid
        )
        .catch(() => {});
    });
    await client
      .send('Target.setAutoAttach', { autoAttach: true, waitForDebuggerOnStart: false, flatten: true })
      .catch(() => {});
  }

  function report(id, target) {
    const rec = clients.get(id);
    if (!rec) return;
    const clean = ((target && target.url) || '').split('#')[0];
    if (rec.url !== clean) {
      rec.url = clean;
      if (onAttach) onAttach(target || { id });
    }
  }

  async function attach(target) {
    if (stopped || clients.has(target.id)) return;
    let client;
    try {
      client = await CDP({ target, port });
    } catch {
      return;
    }
    clients.set(target.id, { client, url: null });
    seen.add(target.id);

    let ctx = null;
    client.on('Runtime.executionContextCreated', (e) => {
      ctx = e.context && e.context.id;
      if (!stopped) evalNow(client).catch(() => {});
    });
    client.on('disconnect', () => {
      clients.delete(target.id);
    });

    const onNav = () => {
      evalNow(client).catch(() => {});
    };
    client.on('Page.loadEventFired', onNav);
    client.on('Page.domContentLoaded', onNav);
    client.on('Page.frameNavigated', (e) => {
      if (e && e.frame && !e.frame.parentId) onNav();
    });
    client.on('Page.navigatedWithinDocument', onNav);

    try {
      await client.Runtime.enable();
    } catch {}
    await armPreload(client);
    await waitForLoad(client);

    try {
      await client.Runtime.evaluate({ expression: source });
    } catch {

      await sleep(350);
      if (ctx != null) {
        await client.Runtime.evaluate({ expression: source, contextId: ctx }).catch(() => {});
      }
    }
    burstEval(client);
    ensureLivePage(client, target).catch(() => {});
    await wireChildFrames(client).catch(() => {});
    report(target.id, target);
  }

  async function scan() {
    if (stopped || scanning) return;
    scanning = true;
    try {
      let targets;
      try {
        targets = await CDP.List({ port });
      } catch {
        return;
      }
      const pages = targets.filter((t) => t.type === 'page');
      const liveIds = new Set(pages.map((t) => t.id));

      for (const id of [...clients.keys()]) {
        if (!liveIds.has(id)) {
          const rec = clients.get(id);
          clients.delete(id);
          try {
            await rec.client.close();
          } catch {}
        }
      }

      for (const t of pages) {
        if (clients.has(t.id)) {
          report(t.id, t);
          evalNow(clients.get(t.id).client).catch(() => {});
        } else {

          await attach(t);
        }
      }
    } finally {
      scanning = false;
    }
  }

  await scan();
  const interval = setInterval(scan, 2000);

  return {
    count: () => seen.size,
    stop() {
      stopped = true;
      clearInterval(interval);
      for (const rec of clients.values()) {
        try {
          rec.client.close();
        } catch {}
      }
      clients.clear();
    },
  };
}
