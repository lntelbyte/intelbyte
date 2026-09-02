import { spawn } from 'child_process';
import { existsSync, readdirSync } from 'fs';
import { basename, dirname, join } from 'path';
import { load, save } from '../../core/config.js';
import { ps, psJson, psAsync, psq } from './ps.js';

const FIRST_PORT = 9300;

function expand(p) {
  return p.replace(/%([^%]+)%/g, (_, v) => process.env[v] || '');
}
function firstExisting(paths) {
  for (const p of paths) {
    const full = expand(p);
    if (full && existsSync(full)) return full;
  }
  return null;
}

const KNOWN = [
  {
    key: 'chrome', label: 'Google Chrome', protocol: 'cdp', kind: 'browser',
    image: 'chrome.exe', chromium: 'chrome',
    paths: [
      '%ProgramFiles%\\Google\\Chrome\\Application\\chrome.exe',
      '%ProgramFiles(x86)%\\Google\\Chrome\\Application\\chrome.exe',
      '%LocalAppData%\\Google\\Chrome\\Application\\chrome.exe',
    ],
  },
  {
    key: 'brave', label: 'Brave', protocol: 'cdp', kind: 'browser',
    image: 'brave.exe', chromium: 'brave',
    paths: [
      '%ProgramFiles%\\BraveSoftware\\Brave-Browser\\Application\\brave.exe',
      '%ProgramFiles(x86)%\\BraveSoftware\\Brave-Browser\\Application\\brave.exe',
      '%LocalAppData%\\BraveSoftware\\Brave-Browser\\Application\\brave.exe',
    ],
  },
  {
    key: 'edge', label: 'Microsoft Edge', protocol: 'cdp', kind: 'browser',
    image: 'msedge.exe', chromium: 'edge',
    paths: [
      '%ProgramFiles(x86)%\\Microsoft\\Edge\\Application\\msedge.exe',
      '%ProgramFiles%\\Microsoft\\Edge\\Application\\msedge.exe',
    ],
  },
  {
    key: 'firefox', label: 'Firefox', protocol: 'bidi', kind: 'firefox',
    image: 'firefox.exe', chromium: null,
    paths: [
      '%ProgramFiles%\\Mozilla Firefox\\firefox.exe',
      '%ProgramFiles(x86)%\\Mozilla Firefox\\firefox.exe',
    ],
  },
  {
    key: 'discord', label: 'Discord', protocol: 'cdp', kind: 'discord',
    image: 'Discord.exe', chromium: null,

    paths: ['%LocalAppData%\\Discord\\Update.exe'],
  },
];

const BROWSER_IMAGES = {
  'chrome.exe': 'chrome',
  'brave.exe': 'brave',
  'msedge.exe': 'edge',
  'firefox.exe': 'firefox',
};

function shortcutRoots() {
  const appData = process.env.APPDATA || '';
  const pub = process.env.PUBLIC || 'C:\\Users\\Public';
  const home = process.env.USERPROFILE || '';
  return [
    join(appData, 'Microsoft', 'Windows', 'Start Menu', 'Programs'),
    'C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs',
    join(home, 'Desktop'),
    join(pub, 'Desktop'),
    join(appData, 'Microsoft', 'Internet Explorer', 'Quick Launch', 'User Pinned', 'TaskBar'),
    join(appData, 'Microsoft', 'Internet Explorer', 'Quick Launch', 'User Pinned', 'ImplicitAppShortcuts'),
  ];
}

function listLnkFiles() {
  const out = [];
  const walk = (dir, depth) => {
    if (depth > 8) return;
    let ents;
    try {
      ents = readdirSync(dir, { withFileTypes: true });
    } catch {
      return;
    }
    for (const e of ents) {
      const p = join(dir, e.name);
      if (e.isDirectory()) walk(p, depth + 1);
      else if (e.isFile() && e.name.toLowerCase().endsWith('.lnk')) out.push(p);
    }
  };
  for (const root of shortcutRoots()) {
    if (existsSync(root)) walk(root, 0);
  }
  return out;
}

function enumerateShortcuts() {
  const lnks = listLnkFiles();
  if (!lnks.length) return [];
  const list = lnks.map((p) => psq(p)).join(',');
  const script =
    `$sh = New-Object -ComObject WScript.Shell; ` +
    `$paths = @(${list}); ` +
    `foreach ($p in $paths) { ` +
      `try { ` +
        `$s = $sh.CreateShortcut($p); ` +
        `if (-not $s.TargetPath) { continue }; ` +
        `Write-Output ($p + [char]9 + $s.TargetPath + [char]9 + ([string]$s.Arguments)) ` +
      `} catch {} ` +
    `}`;
  let raw = '';
  try {
    raw = ps(script);
  } catch {
    return [];
  }
  if (!raw) return [];
  const rows = [];
  for (const line of raw.split(/\r?\n/)) {
    if (!line) continue;
    const i = line.indexOf('\t');
    if (i < 0) continue;
    const j = line.indexOf('\t', i + 1);
    if (j < 0) continue;
    rows.push({
      Lnk: line.slice(0, i),
      Target: line.slice(i + 1, j),
      Arguments: line.slice(j + 1),
    });
  }
  return rows;
}

function isElectron(exe) {
  const dir = dirname(exe);
  return (
    existsSync(join(dir, 'resources', 'app.asar')) ||
    existsSync(join(dir, '..', 'resources', 'app.asar'))
  );
}

function slugify(s) {
  return String(s).toLowerCase().replace(/\.exe$/, '').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}

export function discoverApps() {
  const shortcuts = enumerateShortcuts();
  const byId = new Map();

  const add = (rec) => {
    const prev = byId.get(rec.id);
    if (prev) {

      const seen = new Set(prev.shortcuts.map((s) => s.lnk.toLowerCase()));
      for (const s of rec.shortcuts) if (!seen.has(s.lnk.toLowerCase())) prev.shortcuts.push(s);
      return;
    }
    byId.set(rec.id, rec);
  };

  for (const k of KNOWN) {
    const exe = firstExisting(k.paths);
    if (!exe) continue;
    add({
      id: k.key,
      label: k.label,
      protocol: k.protocol,
      kind: k.kind,
      image: k.image,
      exe,
      launchArgs: k.kind === 'discord' ? ['--processStart', 'Discord.exe'] : [],
      chromium: k.chromium || null,
      shortcuts: [],
    });
  }

  for (const sc of shortcuts) {
    const target = sc.Target;
    if (!target || !existsSync(target)) continue;
    const base = basename(target).toLowerCase();
    const args = sc.Arguments || '';

    let id = null;
    let rec = null;
    if (BROWSER_IMAGES[base]) {

      if (/https?:\/\//i.test(args)) continue;
      id = BROWSER_IMAGES[base];
      if (!byId.has(id)) {
        const k = KNOWN.find((x) => x.key === id);
        rec = { id, label: k.label, protocol: k.protocol, kind: k.kind, image: k.image,
                exe: target, launchArgs: [], chromium: k.chromium || null, shortcuts: [] };
      }
    } else if (base === 'update.exe' && /discord\.exe/i.test(args)) {
      id = 'discord';
      if (!byId.has(id)) {
        rec = { id, label: 'Discord', protocol: 'cdp', kind: 'discord', image: 'Discord.exe',
                exe: target, launchArgs: ['--processStart', 'Discord.exe'], chromium: null, shortcuts: [] };
      }
    } else if (base.endsWith('.exe') && isElectron(target)) {
      id = slugify(basename(target));
      if (!id) continue;
      if (!byId.has(id)) {
        rec = { id, label: sc.Name || basename(target), protocol: 'cdp', kind: 'electron',
                image: basename(target), exe: target, launchArgs: [], chromium: null, shortcuts: [] };
      }
    } else {
      continue;
    }

    if (rec) add(rec);

    const app = byId.get(id);
    if (app) {
      const low = sc.Lnk.toLowerCase();
      if (!app.shortcuts.some((s) => s.lnk.toLowerCase() === low)) {
        app.shortcuts.push({ lnk: sc.Lnk, args });
      }
    }
  }

  return [...byId.values()];
}

export function stripDebug(args) {
  return String(args || '')
    .replace(/--process-start-args\s+"[^"]*"/g, '')
    .replace(/--remote-debugging-port=\d+/g, '')
    .replace(/--remote-allow-origins=\*/g, '')
    .replace(/\s{2,}/g, ' ')
    .trim();
}

function debugArgs(app) {
  const flag = `--remote-debugging-port=${app.port}`;
  if (app.kind === 'discord') {
    return ['--process-start-args', `"${flag}"`];
  }
  if (app.kind === 'browser') {
    return [flag, '--remote-allow-origins=*'];
  }
  return [flag];
}

export function mergeArgs(origArgs, app) {
  const flag = `--remote-debugging-port=${app.port}`;
  if (origArgs.includes(flag)) return origArgs;
  const extra = debugArgs(app).join(' ');
  return (origArgs.trim() + ' ' + extra).trim();
}

function setShortcutArgs(lnk, args) {
  ps(
    `$sh = New-Object -ComObject WScript.Shell; ` +
      `$s = $sh.CreateShortcut(${psq(lnk)}); ` +
      `$s.Arguments = ${psq(args)}; ` +
      `$s.Save()`
  );
}

export function wireShortcuts(app, prevArgsByLnk = new Map()) {
  const wired = [];
  for (const sc of app.shortcuts || []) {
    const key = sc.lnk.toLowerCase();
    const pristine = prevArgsByLnk.has(key) ? prevArgsByLnk.get(key) : stripDebug(sc.args);
    const newArgs = mergeArgs(pristine, app);
    try {
      setShortcutArgs(sc.lnk, newArgs);
      wired.push({ lnk: sc.lnk, origArgs: pristine });
    } catch {}
  }
  return wired;
}

export function unwireShortcuts(registry) {
  const restored = [];
  for (const app of Object.values(registry || {})) {
    for (const sc of app.shortcuts || []) {
      try {
        setShortcutArgs(sc.lnk, sc.origArgs || '');
        restored.push(sc.lnk);
      } catch {}
    }
  }
  return restored;
}

export function setupApps() {
  const cfg = load();
  const prev = cfg.apps || {};
  const found = discoverApps();

  const usedPorts = new Set(Object.values(prev).map((a) => a.port));
  let nextPort = FIRST_PORT;
  const takePort = () => {
    while (usedPorts.has(nextPort)) nextPort++;
    usedPorts.add(nextPort);
    return nextPort;
  };

  const prevArgsByLnk = new Map();
  for (const a of Object.values(prev)) {
    for (const s of a.shortcuts || []) prevArgsByLnk.set(s.lnk.toLowerCase(), s.origArgs || '');
  }

  const apps = {};
  for (const f of found) {
    const old = prev[f.id];
    const app = {
      label: f.label,
      protocol: f.protocol,
      kind: f.kind,
      image: f.image,
      exe: f.exe,
      launchArgs: f.launchArgs || [],
      chromium: f.chromium || null,
      port: old ? old.port : takePort(),
      shortcuts: [],
    };
    app.shortcuts = wireShortcuts({ ...f, port: app.port }, prevArgsByLnk);
    apps[f.id] = app;
  }
  const removedIds = Object.keys(prev).filter((id) => !apps[id]);
  cfg.apps = apps;
  save(cfg);
  return { apps, removedIds };
}

export function runAppArgv(id, extra = []) {
  const cfg = load();
  const app = (cfg.apps || {})[id];
  if (!app) return null;
  if (app.kind === 'discord') {
    return [app.exe, '--processStart', 'Discord.exe',
      '--process-start-args', `--remote-debugging-port=${app.port}`, ...extra];
  }
  const flags = [`--remote-debugging-port=${app.port}`];
  if (app.kind === 'browser') flags.push('--remote-allow-origins=*');
  return [app.exe, ...flags, ...extra];
}

export async function runApp(id, extra = []) {
  const cfg = load();
  const app = (cfg.apps || {})[id];
  if (!app) return false;
  if (app.chromium && cfg.scrubAddressBar) {
    try {
      const { scrubChromium, isBrowserRunning } = await import('./chromium.js');
      if (!isBrowserRunning(app.chromium)) await scrubChromium(cfg, app.chromium);
    } catch {}
  }
  const argv = runAppArgv(id, extra);
  if (!argv) return false;
  try {
    const child = spawn(argv[0], argv.slice(1), {
      detached: true,
      stdio: 'ignore',
      windowsHide: true,
    });
    child.unref();
    return true;
  } catch {
    return false;
  }
}

export async function inspectAppState(app) {
  const image = String(app.image || '').replace(/'/g, '');
  const script =
    `$p = @(Get-CimInstance Win32_Process -Filter "Name='${image}'" -ErrorAction SilentlyContinue); ` +
    `if ($p.Count -eq 0) { 'stopped' } ` +
    `elseif ($p | Where-Object { $_.CommandLine -like '*remote-debugging-port=${app.port}*' }) { 'protected' } ` +
    `else { ` +
      `$main = @($p | Where-Object { $_.CommandLine -and $_.CommandLine -notlike '*--type=*' }); ` +
      `if ($main.Count -eq 0) { 'stopped' } else { 'unprotected' } ` +
    `}`;
  try {
    const out = (await psAsync(script)).trim();
    if (out === 'protected' || out === 'unprotected' || out === 'stopped') return out;
    return 'stopped';
  } catch {
    return 'stopped';
  }
}

export async function runningUnprotected(id, app) {
  return (await inspectAppState(app)) === 'unprotected';
}

export async function killApp(app) {
  await psAsync(`Start-Process taskkill -ArgumentList '/F','/T','/IM','${app.image}' -WindowStyle Hidden -Wait`).catch(() => {});
}

async function waitUntilStopped(app, maxMs = 12000) {
  const deadline = Date.now() + maxMs;
  while (Date.now() < deadline) {
    if ((await inspectAppState(app)) === 'stopped') return true;
    await new Promise((r) => setTimeout(r, 350));
  }
  return (await inspectAppState(app)) === 'stopped';
}

export async function relaunchApp(id) {
  const cfg = load();
  const app = (cfg.apps || {})[id];
  if (!app) return false;
  await killApp(app);
  await waitUntilStopped(app);
  const settle = app.kind === 'browser' ? 2800 : 1200;
  await new Promise((r) => setTimeout(r, settle));
  return runApp(id);
}

export async function relaunchUnprotectedApps() {
  const cfg = load();
  const apps = cfg.apps || {};
  const relaunched = [];
  for (const [id, app] of Object.entries(apps)) {
    if (app.kind === 'browser' || app.kind === 'firefox') continue;
    const state = await inspectAppState(app);
    if (state !== 'unprotected') continue;
    if (await relaunchApp(id)) relaunched.push(app.label);
  }
  return relaunched;
}
