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

  async function wireChildFrames(client) {
    client.on('Target.attachedToTarget', async (params) => {
      const sid = params && params.sessionId;
      if (!sid || stopped) return;
      await armPreload(client, sid);
      await evalNow(client, sid);
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
    });
    client.on('disconnect', () => {
      clients.delete(target.id);
    });

    const onNav = () => {
      evalNow(client).catch(() => {});
    };
    client.on('Page.loadEventFired', onNav);
    client.on('Page.frameNavigated', (e) => {
      if (e && e.frame && !e.frame.parentId) onNav();
    });

    try {
      await client.Runtime.enable();
    } catch {}
    await armPreload(client);

    try {
      await client.Runtime.evaluate({ expression: source });
    } catch {

      await sleep(350);
      if (ctx != null) {
        await client.Runtime.evaluate({ expression: source, contextId: ctx }).catch(() => {});
      }
    }
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
