import { buildPairs, buildPayloadData, load, maskValue } from './core/config.js';
import { buildPayload } from './core/payload.js';
import { probePort, startInjector } from './core/cdp.js';
import { startBidiInjector } from './core/bidi.js';
import { banner, title, ok, warn, info, line, c, sleep } from './core/ui.js';

export async function runShield(platform, cfg, opts = {}) {
  const quiet = !!opts.quiet;
  const emit = opts.onEvent || (() => {});
  let currentCfg = cfg;
  let apps = currentCfg.apps || {};
  let ids = Object.keys(apps);
  let pairs = buildPairs(currentCfg);
  let source = buildPayload(buildPayloadData(currentCfg));
  let sourceSignature = JSON.stringify(buildPayloadData(currentCfg));
  let autoRelaunch = currentCfg.autoRelaunch !== false;

  if (!quiet) {
    await banner();
    title('intelbyte • shield');
  }

  if (!pairs.length) {
    if (!quiet)
      warn(
        'Nothing to protect yet. Add first: ' +
          c.cyan('intelbyte protect-mail <mail>') +
          c.gray(' / ') +
          c.cyan('intelbyte protect-phone <number>')
      );
    emit({ type: 'idle', reason: 'no-entries' });
  }
  if (!ids.length) {
    if (!quiet) warn('No apps wired yet. One-time: ' + c.cyan('intelbyte setup'));
    emit({ type: 'idle', reason: 'no-apps' });
  }

  if (!quiet) {
    ok('Masked entries:');
    for (const [real, fake] of pairs) {
      line(`  ${c.gray(maskValue(real))} ${c.gray('→')} ${c.green(fake)}`);
    }
    line('');
    ok('Watching:');
    if (ids.length) {
      const w = Math.max(...ids.map((id) => apps[id].label.length));
      for (const id of ids) {
        const a = apps[id];
        line(`  ${c.cyan(a.label.padEnd(w))}  ${c.gray(`port ${a.port} · ${a.protocol}`)}`);
      }
    } else {
      line(c.gray('  No wired apps yet — setup will be picked up automatically.'));
    }
    line('');
    if (autoRelaunch) {
      line(
        c.gray('  Discord / Electron launched without the debug port get reopened once.') +
          '\n' +
          c.gray('  Browsers are left alone — open them yourself; the shield attaches.')
      );
      line('');
    }
  }

  const active = new Map();
  const starting = new Set();
  const misses = new Map();
  const unprotHits = new Map();
  const relaunchTries = new Map();
  const busy = new Set();
  const gaveUp = new Set();
  const MAX_TRIES = 3;
  let stopped = false;

  function sameApp(a, b) {
    return !!a && !!b && a.port === b.port && a.protocol === b.protocol && a.image === b.image;
  }

  function refreshConfig() {
    let latest;
    try {
      latest = load();
    } catch {
      return;
    }

    const latestApps = latest.apps || {};
    const latestIds = Object.keys(latestApps);
    for (const id of [...active.keys()]) {
      if (!sameApp(apps[id], latestApps[id])) {
        try { active.get(id).stop(); } catch {}
        active.delete(id);
      }
    }
    apps = latestApps;
    ids = latestIds;
    autoRelaunch = latest.autoRelaunch !== false;

    const latestData = buildPayloadData(latest);
    const latestSignature = JSON.stringify(latestData);
    if (latestSignature !== sourceSignature) {
      sourceSignature = latestSignature;
      source = buildPayload(latestData);
      for (const inj of active.values()) {
        try { inj.update(source); } catch {}
      }
    }
    currentCfg = latest;
    pairs = buildPairs(latest);
  }

  async function ensureProtected(id) {
    const a = apps[id];
    if (busy.has(id) || gaveUp.has(id)) return;
    const state = await platform.inspectAppState(a);
    if (state === 'stopped') {
      unprotHits.delete(id);
      relaunchTries.delete(id);
      return;
    }
    if (state === 'protected') {
      unprotHits.delete(id);
      return;
    }
    const hits = (unprotHits.get(id) || 0) + 1;
    unprotHits.set(id, hits);
    if (hits < 2) return;
    unprotHits.delete(id);

    const tries = relaunchTries.get(id) || 0;
    if (tries >= MAX_TRIES) {
      gaveUp.add(id);
      if (!quiet)
        warn(
          `${a.label}: couldn't get it into debug mode after ${MAX_TRIES} tries — giving up. ` +
            'Try ' + c.cyan('intelbyte run-app ' + id) + ' manually.'
        );
      emit({ type: 'gaveup', id, label: a.label });
      return;
    }
    busy.add(id);
    relaunchTries.set(id, tries + 1);
    if (!quiet) warn(`${a.label} is open WITHOUT protection — closing & reopening it in debug mode…`);
    emit({ type: 'relaunch', id, label: a.label });
    try {
      await platform.relaunchApp(id);
    } catch {} finally {
      busy.delete(id);
    }
  }

  async function tick() {
    if (stopped) return;
    refreshConfig();
    if (opts.isPaused && opts.isPaused()) {
      emit({ type: 'paused', connected: [...active.keys()] });
      return;
    }
    const states = await Promise.all(
      ids.map((id) => probePort(apps[id].port).then((live) => [id, live]))
    );
    for (const [id, live] of states) {
      if (stopped) return;
      const a = apps[id];
      if (live) {
        misses.delete(id);
        unprotHits.delete(id);
        relaunchTries.delete(id);
        gaveUp.delete(id);
        if (active.has(id) || starting.has(id)) continue;
        starting.add(id);
        const start = a.protocol === 'bidi' ? startBidiInjector : startInjector;
        start(a.port, source, (t) => {
          if (!quiet)
            info(`${a.label}: masked ${c.gray((t.url || t.title || t.id || '').slice(0, 90))}`);
          emit({ type: 'masked', id, label: a.label, url: t.url || t.title || t.id || '' });
        })
          .then((inj) => {
            if (stopped) return inj.stop();
            active.set(id, inj);
            if (!quiet) ok(`${a.label} ${c.bold('connected')} — live masking on.`);
            emit({ type: 'connected', id, label: a.label });
          })
          .catch(() => {})
          .finally(() => starting.delete(id));
      } else if (active.has(id)) {
        const m = (misses.get(id) || 0) + 1;
        misses.set(id, m);
        if (m >= 2) {
          active.get(id).stop();
          active.delete(id);
          misses.delete(id);
          if (!quiet) info(`${a.label} closed — waiting for its next launch.`);
          emit({ type: 'closed', id, label: a.label });
        }
      } else if (autoRelaunch && a.kind !== 'browser' && a.kind !== 'firefox') {
        await ensureProtected(id);
      }
    }
    emit({ type: 'tick', connected: [...active.keys()] });
  }

  let ticking = false;
  const safeTick = async () => {
    if (ticking) return;
    ticking = true;
    try {
      await tick();
    } finally {
      ticking = false;
    }
  };
  await safeTick();
  const interval = setInterval(safeTick, 1500);

  if (!quiet) {
    ok(c.bold('Shield active.') + ' Open any wired app, whenever — it attaches by itself.');
    line(c.gray('  Keep this window open. Stop with ') + c.bold('Ctrl+C') + c.gray('.'));
  }
  emit({ type: 'active', apps: ids.length });

  const stop = () => {
    if (stopped) return;
    stopped = true;
    clearInterval(interval);
    for (const inj of active.values()) inj.stop();
    emit({ type: 'stopped' });
  };

  return { stop, active: () => [...active.keys()] };
}

export async function runShieldForeground(platform, cfg) {
  const handle = await runShield(platform, cfg);
  if (!handle) return;

  const shutdown = () => {
    handle.stop();
    line('');
    info('Shield stopped. (Open windows stay masked until they reload.)');
    process.exit(0);
  };
  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);

  await new Promise(() => {});
}
