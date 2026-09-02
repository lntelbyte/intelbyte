import WebSocket from 'ws';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function open(url) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(url);
    ws.once('open', () => resolve(ws));
    ws.once('error', reject);
  });
}

export async function startBidiInjector(port, source, onAttach) {
  let ws;
  try {
    ws = await open(`ws://127.0.0.1:${port}/session`);
  } catch {
    ws = await open(`ws://127.0.0.1:${port}/`);
  }

  let nextId = 1;
  const pending = new Map();
  const handlers = {};
  let stopped = false;

  ws.on('message', (data) => {
    let msg;
    try {
      msg = JSON.parse(data.toString());
    } catch {
      return;
    }
    if (msg.id && pending.has(msg.id)) {
      const p = pending.get(msg.id);
      pending.delete(msg.id);
      if (msg.error) p.reject(new Error(msg.error + ': ' + (msg.message || '')));
      else p.resolve(msg.result);
    } else if (msg.method && handlers[msg.method]) {
      handlers[msg.method](msg.params || {});
    }
  });

  function cmd(method, params = {}) {
    const id = nextId++;
    return new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject });
      try {
        ws.send(JSON.stringify({ id, method, params }));
      } catch (e) {
        pending.delete(id);
        reject(e);
        return;
      }
      setTimeout(() => {
        if (pending.has(id)) {
          pending.delete(id);
          reject(new Error('timeout ' + method));
        }
      }, 8000);
    });
  }

  let ok = false;
  for (let i = 0; i < 10 && !ok; i++) {
    try {
      await cmd('session.new', { capabilities: {} });
      ok = true;
    } catch {
      await sleep(500);
    }
  }
  if (!ok) {
    try {
      ws.close();
    } catch {}
    throw new Error('BiDi session.new failed');
  }

  const seen = new Map();

  async function injectContext(ctxId, url) {
    if (!ctxId || stopped) return;
    try {
      await cmd('script.evaluate', {
        expression: source,
        target: { context: ctxId },
        awaitPromise: false,
      });
      const cleanUrl = (url || '').split('#')[0];
      if (cleanUrl && seen.get(ctxId) !== cleanUrl) {
        seen.set(ctxId, cleanUrl);
        if (onAttach) onAttach({ url: url || ctxId });
      }
    } catch {}
  }

  await cmd('script.addPreloadScript', { functionDeclaration: '() => { ' + source + ' }' }).catch(
    () => {}
  );

  handlers['browsingContext.contextCreated'] = (p) => injectContext(p.context, p.url);
  handlers['browsingContext.domContentLoaded'] = (p) => injectContext(p.context, p.url);
  handlers['browsingContext.load'] = (p) => injectContext(p.context, p.url);

  await cmd('session.subscribe', {
    events: [
      'browsingContext.contextCreated',
      'browsingContext.domContentLoaded',
      'browsingContext.load',
    ],
  }).catch(() => {});

  async function sweep() {
    if (stopped) return;
    let tree;
    try {
      tree = await cmd('browsingContext.getTree', {});
    } catch {
      return;
    }
    const flat = [];
    (function walk(nodes) {
      for (const n of nodes || []) {
        flat.push(n);
        if (n.children) walk(n.children);
      }
    })(tree.contexts || []);
    for (const n of flat) {

      await injectContext(n.context, n.url);
    }
  }

  await sweep();
  const interval = setInterval(sweep, 2000);

  return {
    count: () => seen.size,
    stop() {
      stopped = true;
      clearInterval(interval);
      try {
        ws.close();
      } catch {}
    },
  };
}
