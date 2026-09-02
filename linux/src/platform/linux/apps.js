import { execFile, spawn } from 'child_process';
import { promisify } from 'util';
import {
  existsSync,
  readFileSync,
  writeFileSync,
  readdirSync,
  mkdirSync,
  unlinkSync,
  realpathSync,
} from 'fs';
import { homedir } from 'os';
import { join, dirname, basename, isAbsolute } from 'path';
import { fileURLToPath } from 'url';
import { load, save } from '../../core/config.js';

const pexec = promisify(execFile);

const HOME = homedir();

const MULLVAD_EXCLUDE = '/usr/bin/mullvad-exclude';
function splitTunnelPrefix(mode = load().discordSplitTunnel) {
  if (mode === false) return [];
  if (!existsSync(MULLVAD_EXCLUDE)) return [];
  if (mode === true) return [MULLVAD_EXCLUDE];
  try {
    if (readdirSync('/sys/class/net').some((n) => n.includes('mullvad'))) return [MULLVAD_EXCLUDE];
  } catch {}
  return [];
}

const REPO_ROOT = dirname(dirname(dirname(dirname(fileURLToPath(import.meta.url)))));
const BIN_JS = join(REPO_ROOT, 'bin', 'intelbyte.js');
export const OVERRIDE_DIR = join(HOME, '.local', 'share', 'applications');
const MARKER = 'X-Intelbyte-Managed=true';
const FIRST_PORT = 9300;

const DESKTOP_DIRS = [
  '/usr/share/applications',
  '/usr/local/share/applications',
  '/var/lib/flatpak/exports/share/applications',
  join(HOME, '.local/share/flatpak/exports/share/applications'),
  OVERRIDE_DIR,
];

const FLATPAK_APP_DIRS = ['/var/lib/flatpak/app', join(HOME, '.local/share/flatpak/app')];

const FLATPAK_KNOWN = {
  'com.discordapp.Discord': { protocol: 'cdp', splitTunnel: true, commands: ['discord'] },
  'dev.vencord.Vesktop': { protocol: 'cdp', splitTunnel: true, commands: ['vesktop', 'discord'] },
  'io.github.spacingbat3.webcord': { protocol: 'cdp', splitTunnel: true, commands: ['webcord', 'discord'] },
  'com.google.Chrome': { protocol: 'cdp', chromium: 'chrome' },
  'org.chromium.Chromium': { protocol: 'cdp', chromium: 'chromium' },
  'com.brave.Browser': { protocol: 'cdp', chromium: 'brave' },
  'com.microsoft.Edge': { protocol: 'cdp', chromium: 'edge' },
  'org.mozilla.firefox': { protocol: 'bidi', commands: ['firefox'] },
  'com.visualstudio.code': { protocol: 'cdp', commands: ['code'] },
  'com.vscodium.codium': { protocol: 'cdp', commands: ['codium'] },
  'com.slack.Slack': { protocol: 'cdp', commands: ['slack'] },
  'com.spotify.Client': { protocol: 'cdp', commands: ['spotify'] },
  'md.obsidian.Obsidian': { protocol: 'cdp', commands: ['obsidian'] },
  'org.signal.Signal': { protocol: 'cdp', commands: ['signal', 'signal-desktop'] },
  'io.element.Element': { protocol: 'cdp', commands: ['element', 'element-desktop'] },
  'com.getpostman.Postman': { protocol: 'cdp', commands: ['postman'] },
};

const BROWSER_BIN = [
  [/^google-chrome(-stable|-beta|-unstable)?$/, 'cdp', 'chrome', 'chrome'],
  [/^chromium(-browser)?$/, 'cdp', 'chromium', 'chromium'],
  [/^chrome$/, 'cdp', 'chrome', 'chrome'],
  [/^brave(-browser)?(-\w+)?$/, 'cdp', 'brave', 'brave'],
  [/^(microsoft-edge|msedge)(-stable|-beta|-dev)?$/, 'cdp', 'edge', 'edge'],
  [/^firefox(-esr|-bin|-nightly|-beta)?$/, 'bidi', null, 'firefox'],
];

const BIN_ALIASES = {
  chrome: ['google-chrome', 'google-chrome-stable', 'chrome'],
  chromium: ['chromium', 'chromium-browser'],
  brave: ['brave', 'brave-browser'],
  edge: ['microsoft-edge', 'microsoft-edge-stable', 'msedge'],
  firefox: ['firefox', 'firefox-esr'],
};

const FIELD_CODE = /^%[uUfFick]$/;

function tokenize(execLine) {

  const out = [];
  const re = /"([^"]*)"|(\S+)/g;
  let m;
  while ((m = re.exec(execLine))) out.push(m[1] !== undefined ? m[1] : m[2]);
  return out;
}

function parseDesktop(text) {
  let section = null;
  let name = null;
  let mainExec = null;
  let managed = false;
  for (const raw of text.split('\n')) {
    const l = raw.trim();
    if (l.startsWith('[')) section = l;
    else if (l === MARKER) managed = true;
    else if (section === '[Desktop Entry]') {
      if (l.startsWith('Name=') && !name) name = l.slice(5);
      else if (l.startsWith('Exec=') && !mainExec) mainExec = l.slice(5);
    }
  }
  return { name, mainExec, managed };
}

function hasAsarUnder(dir, depth) {
  if (depth < 0) return false;
  let entries;
  try {
    entries = readdirSync(dir, { withFileTypes: true });
  } catch {
    return false;
  }
  for (const e of entries) {
    if (e.isFile() && e.name === 'app.asar') return true;
    if (e.isDirectory() && hasAsarUnder(join(dir, e.name), depth - 1)) return true;
  }
  return false;
}

function flatpakIsElectron(id) {
  for (const base of FLATPAK_APP_DIRS) {
    const files = join(base, id, 'current', 'active', 'files');
    if (existsSync(files) && hasAsarUnder(files, 3)) return true;
  }
  return false;
}

async function whichBin(tok) {
  if (isAbsolute(tok)) {
    try {
      return existsSync(tok) ? realpathSync(tok) : null;
    } catch {
      return null;
    }
  }
  try {
    const { stdout } = await pexec('which', [tok]);
    const p = stdout.trim();
    return p ? realpathSync(p) : null;
  } catch {
    return null;
  }
}

async function classifyExec(tokens) {
  let t = [...tokens];
  while (t.length && (t[0] === 'env' || /^[A-Za-z_][A-Za-z0-9_]*=/.test(t[0]))) t.shift();
  if (!t.length) return null;

  if (basename(t[0]) === 'flatpak' && t.includes('run')) {
    const runIdx = t.indexOf('run');
    let id = null;
    for (let i = runIdx + 1; i < t.length; i++) {
      if (!t[i].startsWith('-') && !FIELD_CODE.test(t[i]) && !t[i].startsWith('@@')) {
        id = t[i];
        break;
      }
    }
    if (!id) return null;
    const known = FLATPAK_KNOWN[id];
    if (!known && !flatpakIsElectron(id)) return null;
    const head = [];
    for (const tok of t) {
      if (tok === '--file-forwarding' || tok.startsWith('@@') || FIELD_CODE.test(tok)) continue;
      head.push(tok);
      if (tok === id) break;
    }
    const slug = id.split('.').pop().toLowerCase();
    const chromium = (known && known.chromium) || null;
    const commands = [
      ...new Set([
        ...((known && known.commands) || [slug]),
        ...((chromium && BIN_ALIASES[chromium]) || []),
      ]),
    ];
    return {
      protocol: (known && known.protocol) || 'cdp',
      kind: 'flatpak',
      exec: head,
      target: 'flatpak:' + id,
      chromium,
      commands,
      splitTunnel: !!(known && known.splitTunnel),
      slug,
      tail: t.slice(t.indexOf(id) + 1).filter((x) => FIELD_CODE.test(x)),
    };
  }

  const bin = await whichBin(t[0]);
  if (!bin) return null;
  const name = basename(bin).toLowerCase();
  const args = t.slice(1);
  let isBrowser = false;
  let protocol = null;
  let chromium = null;
  let family = null;
  for (const [re, proto, chromeKey, fam] of BROWSER_BIN) {
    if (re.test(name)) {
      isBrowser = true;
      protocol = proto;
      chromium = chromeKey;
      family = fam;
      break;
    }
  }
  if (isBrowser && args.some((a) => /^https?:\/\//i.test(a))) return null;
  if (!protocol) {
    const dir = dirname(bin);
    const spots = [join(dir, 'resources', 'app.asar'), join(dir, '..', 'resources', 'app.asar')];
    if (spots.some((p) => existsSync(p))) protocol = 'cdp';
  }
  if (!protocol) return null;
  const looksDiscord = /discord|vesktop|webcord/.test(name);
  const commands = [...new Set([name, ...((family && BIN_ALIASES[family]) || [])])];
  return {
    protocol,
    kind: 'native',
    exec: [t[0]],
    target: bin,
    chromium,
    commands,
    splitTunnel: looksDiscord,
    slug: name.replace(/[^a-z0-9]+/g, '-'),
    tail: args.filter((x) => !x.startsWith('@@')),
  };
}

export async function scanApps() {
  const byFile = new Map();
  for (const dir of DESKTOP_DIRS) {
    let names;
    try {
      names = readdirSync(dir);
    } catch {
      continue;
    }
    for (const n of names) {
      if (!n.endsWith('.desktop')) continue;
      const path = join(dir, n);
      let text;
      try {
        text = readFileSync(path, 'utf8');
      } catch {
        continue;
      }
      let parsed = parseDesktop(text);
      if (parsed.managed) {
        const orig = path + '.intelbyte-orig';
        if (existsSync(orig)) {
          try {
            text = readFileSync(orig, 'utf8');
            parsed = parseDesktop(text);
          } catch {
            continue;
          }
        } else {
          continue;
        }
      }
      byFile.set(n, { path, text, parsed });
    }
  }

  const found = [];
  const usedSlugs = new Set();
  const seenTargets = new Set();
  for (const [file, { path, text, parsed }] of byFile) {
    if (!parsed.mainExec) continue;
    const cls = await classifyExec(tokenize(parsed.mainExec));
    if (!cls) continue;
    if (cls.target && seenTargets.has(cls.target)) continue;
    if (cls.target) seenTargets.add(cls.target);
    let slug = cls.slug;
    while (usedSlugs.has(slug)) slug += '2';
    usedSlugs.add(slug);
    found.push({
      id: slug,
      label: parsed.name || slug,
      protocol: cls.protocol,
      kind: cls.kind,
      exec: cls.exec,
      tail: cls.tail,
      chromium: cls.chromium || null,
      commands: cls.commands || [],
      splitTunnel: cls.splitTunnel,
      desktopFile: file,
      sourcePath: path,
      sourceText: text,
    });
  }
  return found;
}

function launcherCmd() {
  const link = join(HOME, '.local', 'bin', 'intelbyte');
  if (existsSync(link)) return link;
  return `node ${BIN_JS}`;
}

function rewriteExec(execLine, app) {
  const t = tokenize(execLine);
  let tail;
  if (app.kind === 'flatpak') {
    const idIdx = t.indexOf(app.exec[app.exec.length - 1]);
    tail = idIdx >= 0 ? t.slice(idIdx + 1) : [];
  } else {
    tail = t.slice(1);
  }
  tail = tail.filter((x) => !x.startsWith('@@') && x !== '--file-forwarding');
  return `Exec=${launcherCmd()} run-app ${app.id}${tail.length ? ' ' + tail.join(' ') : ''}`;
}

export function installOverride(app) {
  mkdirSync(OVERRIDE_DIR, { recursive: true });
  const dest = join(OVERRIDE_DIR, app.desktopFile);
  if (app.sourcePath === dest && !existsSync(dest + '.intelbyte-orig')) {
    writeFileSync(dest + '.intelbyte-orig', app.sourceText);
  }
  const out = [];
  let section = null;
  for (const raw of app.sourceText.split('\n')) {
    const l = raw.trim();
    if (l.startsWith('[')) {
      section = l;
      out.push(raw);
      if (section === '[Desktop Entry]') out.push(MARKER);
      continue;
    }
    if (l.startsWith('Exec=')) {
      out.push(rewriteExec(l.slice(5), app));
      continue;
    }
    if (l.startsWith('DBusActivatable=')) {
      out.push('DBusActivatable=false');
      continue;
    }
    if (l === MARKER) continue;
    out.push(raw);
  }
  writeFileSync(dest, out.join('\n'));
  return dest;
}

const BIN_DIR = join(HOME, '.local', 'bin');
const SHIM_MARK = '# intelbyte-shim';

function selfRunner() {
  const self = join(BIN_DIR, 'intelbyte');
  if (existsSync(self)) return self;
  return `node ${BIN_JS}`;
}

export function installShims(id, commands) {
  if (!commands || !commands.length) return [];
  mkdirSync(BIN_DIR, { recursive: true });
  const runner = selfRunner();
  const installed = [];
  for (const name of commands) {
    if (name === 'intelbyte') continue;
    const dest = join(BIN_DIR, name);
    if (existsSync(dest)) {
      let head = '';
      try {
        head = readFileSync(dest, 'utf8');
      } catch {
        continue;
      }
      if (!head.includes(SHIM_MARK)) continue;
    }
    const body =
      `#!/bin/sh\n${SHIM_MARK} — opens this app with intelbyte screen-privacy on.\n` +
      `exec ${runner} run-app ${id} "$@"\n`;
    try {
      writeFileSync(dest, body, { mode: 0o755 });
      installed.push(dest);
    } catch {}
  }
  return installed;
}

export function removeShims(registry) {
  const removed = [];
  for (const app of Object.values(registry || {})) {
    for (const dest of app.shims || []) {
      if (!existsSync(dest)) continue;
      try {
        if (!readFileSync(dest, 'utf8').includes(SHIM_MARK)) continue;
        unlinkSync(dest);
        removed.push(dest);
      } catch {}
    }
  }
  return removed;
}

export function removeOverrides(registry) {
  const removed = [];
  for (const app of Object.values(registry || {})) {
    const dest = app.override;
    if (!dest || !existsSync(dest)) continue;
    try {
      if (!readFileSync(dest, 'utf8').includes(MARKER)) continue;
      const orig = dest + '.intelbyte-orig';
      if (existsSync(orig)) {
        writeFileSync(dest, readFileSync(orig, 'utf8'));
        unlinkSync(orig);
      } else {
        unlinkSync(dest);
      }
      removed.push(dest);
    } catch {}
  }
  refreshDesktopDatabase();
  return removed;
}

export function refreshDesktopDatabase() {
  try {
    spawn('update-desktop-database', [OVERRIDE_DIR], { stdio: 'ignore' }).unref();
  } catch {}
}

export async function setupApps() {
  const cfg = load();
  const prev = cfg.apps || {};
  const found = await scanApps();

  const usedPorts = new Set(Object.values(prev).map((a) => a.port));
  let nextPort = FIRST_PORT;
  const takePort = () => {
    while (usedPorts.has(nextPort)) nextPort++;
    usedPorts.add(nextPort);
    return nextPort;
  };

  const apps = {};
  for (const f of found) {
    const old = prev[f.id];
    const app = {
      label: f.label,
      protocol: f.protocol,
      kind: f.kind,
      exec: f.exec,
      chromium: f.chromium || null,
      commands: f.commands || [],
      splitTunnel: f.splitTunnel,
      port: old ? old.port : takePort(),
      desktopFile: f.desktopFile,
      override: null,
      shims: [],
    };
    app.override = installOverride({ ...f, ...app, id: f.id });
    app.shims = installShims(f.id, app.commands);
    apps[f.id] = app;
  }
  const removedIds = Object.keys(prev).filter((id) => !apps[id]);
  cfg.apps = apps;
  save(cfg);
  refreshDesktopDatabase();
  return { apps, removedIds };
}

export function runAppArgv(id, extra = []) {
  const cfg = load();
  const app = (cfg.apps || {})[id];
  if (!app) return null;
  const pre = app.splitTunnel ? splitTunnelPrefix() : [];
  return [...pre, ...app.exec, `--remote-debugging-port=${app.port}`, ...extra];
}

export async function runApp(id, extra = []) {
  const cfg = load();
  const app = (cfg.apps || {})[id];
  if (!app) return false;
  if (app.chromium) {
    try {
      const { scrubChromium, isBrowserRunning } = await import('./chromium.js');
      if (!isBrowserRunning(app.chromium)) await scrubChromium(cfg, app.chromium);
    } catch {}
  }
  const argv = runAppArgv(id, extra);
  if (!argv) return false;
  const child = spawn(argv[0], argv.slice(1), { detached: true, stdio: 'ignore' });
  child.unref();
  return true;
}

async function pgrepF(pattern) {
  try {
    await pexec('pgrep', ['-f', pattern]);
    return true;
  } catch {
    return false;
  }
}

function appNeedle(app) {
  return app.kind === 'flatpak' ? app.exec[app.exec.length - 1] : app.exec[0];
}

export async function inspectAppState(app) {
  if (!(await pgrepF(appNeedle(app)))) return 'stopped';
  const hasFlag = await pgrepF(`remote-debugging-port=${app.port}`);
  return hasFlag ? 'protected' : 'unprotected';
}

export async function runningUnprotected(id, app) {
  return (await inspectAppState(app)) === 'unprotected';
}

export async function killApp(app) {
  if (app.kind === 'flatpak') {
    await pexec('flatpak', ['kill', app.exec[app.exec.length - 1]]).catch(() => {});
  } else {
    await pexec('pkill', ['-f', app.exec[0]]).catch(() => {});
  }
}
