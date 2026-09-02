import { homedir } from 'os';
import { join } from 'path';
import { mkdirSync, readFileSync, writeFileSync } from 'fs';
import { randomInt } from 'crypto';

function baseDir() {
  if (process.env.INTELBYTE_HOME) return process.env.INTELBYTE_HOME;
  if (process.platform === 'win32') {
    const appData = process.env.APPDATA || join(homedir(), 'AppData', 'Roaming');
    return join(appData, 'intelbyte');
  }
  const xdg = process.env.XDG_CONFIG_HOME || join(homedir(), '.config');
  return join(xdg, 'intelbyte');
}

const DIR = baseDir();
const FILE = join(DIR, 'config.json');

const DEFAULTS = {
  version: 3,
  discordSplitTunnel: 'auto',
  autoRelaunch: true,
  emails: [],
  phones: [],
  customs: [],
  apps: {},
};

export function configPath() {
  return FILE;
}

export function configDir() {
  return DIR;
}

export function load() {
  try {
    const data = JSON.parse(readFileSync(FILE, 'utf8'));
    return {
      ...DEFAULTS,
      ...data,
      emails: Array.isArray(data.emails) ? data.emails : [],
      phones: Array.isArray(data.phones) ? data.phones : [],
      customs: Array.isArray(data.customs) ? data.customs : [],
      apps: data.apps && typeof data.apps === 'object' ? data.apps : {},
    };
  } catch {
    return { ...DEFAULTS, emails: [], phones: [], customs: [], apps: {} };
  }
}

export function save(cfg) {
  mkdirSync(DIR, { recursive: true });
  writeFileSync(FILE, JSON.stringify(cfg, null, 2) + '\n');
}

export function isEmail(s) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(s);
}
export function phoneDigits(s) {
  return String(s).replace(/\D/g, '');
}
export function isPhone(s) {
  const t = String(s).trim();
  if (!/^[+\d][\d\s().-]*$/.test(t)) return false;
  const d = phoneDigits(t);
  return d.length >= 7 && d.length <= 15;
}

export function isText(s) {
  return String(s).trim().length >= 2;
}

const normEmail = (s) => String(s).trim().toLowerCase();
const normPhone = (s) => String(s).trim();
const normText = (s) => String(s).trim();

const ALPHA = 'abcdefghijklmnopqrstuvwxyz';
const ALNUM = ALPHA + '0123456789';

export function makeFakeEmail(real) {
  const at = real.lastIndexOf('@');
  const domain = at >= 0 ? real.slice(at + 1) : 'gmail.com';
  const local = at >= 0 ? real.slice(0, at) : real;
  const len = Math.min(11, Math.max(5, local.length));
  let s = ALPHA[randomInt(ALPHA.length)];
  for (let i = 1; i < len; i++) s += ALNUM[randomInt(ALNUM.length)];
  return `${s}@${domain}`;
}

export function makeFakePhone(real) {

  return String(real).replace(/\d/g, () => String(randomInt(10)));
}

export function makeFakeText(real) {
  return String(real).replace(/./gu, (ch) => {
    if (/[a-z]/.test(ch)) return ALPHA[randomInt(ALPHA.length)];
    if (/[A-Z]/.test(ch)) return ALPHA[randomInt(ALPHA.length)].toUpperCase();
    if (/[0-9]/.test(ch)) return String(randomInt(10));
    if (/\p{L}/u.test(ch)) return ALPHA[randomInt(ALPHA.length)];
    return ch;
  });
}

export function maskEmail(email) {
  const at = email.lastIndexOf('@');
  if (at < 1) return email.replace(/./g, '*');
  const local = email.slice(0, at);
  const domain = email.slice(at);
  const masked = local.length <= 2 ? local[0] + '*' : local[0] + '***' + local[local.length - 1];
  return masked + domain;
}

export function maskPhone(p) {
  const digits = (p.match(/\d/g) || []).length;
  if (digits <= 4) return p.replace(/\d/g, '*');
  let seen = 0;
  return p.replace(/\d/g, (d) => {
    seen++;
    return seen <= 2 || seen > digits - 2 ? d : '*';
  });
}

export function maskText(s) {
  s = String(s);
  if (s.length <= 2) return s[0] + '*';
  return s[0] + '*'.repeat(Math.min(3, s.length - 2)) + s[s.length - 1];
}

export function maskValue(v, kind) {
  if (kind === 'customs') return maskText(v);
  return String(v).includes('@') ? maskEmail(v) : maskPhone(v);
}

const KINDS = {
  emails: { valid: isEmail, norm: normEmail, fake: makeFakeEmail, label: 'email' },
  phones: { valid: isPhone, norm: normPhone, fake: makeFakePhone, label: 'phone' },
  customs: { valid: isText, norm: normText, fake: makeFakeText, label: 'text' },
};

export function addRandom(kind, inputs) {
  const spec = KINDS[kind];
  const cfg = load();
  const added = [];
  const skipped = [];
  for (const raw of inputs) {
    const real = spec.norm(raw);
    if (!spec.valid(real)) {
      skipped.push({ value: raw, reason: `invalid ${spec.label} format` });
      continue;
    }
    if (cfg[kind].find((e) => e.real === real)) {
      skipped.push({ value: real, reason: 'already protected' });
      continue;
    }
    const entry = { real, fake: spec.fake(real) };
    cfg[kind].push(entry);
    added.push(entry);
  }
  save(cfg);
  return { added, skipped };
}

export function addCustom(kind, pairs) {
  const spec = KINDS[kind];
  const cfg = load();
  const added = [];
  const skipped = [];
  for (const [rawReal, rawFake] of pairs) {
    const real = spec.norm(rawReal);
    const fake = spec.norm(rawFake);
    if (!spec.valid(real)) {
      skipped.push({ value: rawReal, reason: `invalid ${spec.label} (real)` });
      continue;
    }
    if (!spec.valid(fake)) {
      skipped.push({ value: rawFake, reason: `invalid ${spec.label} (fake)` });
      continue;
    }
    const existing = cfg[kind].find((e) => e.real === real);
    if (existing) {
      existing.fake = fake;
      added.push(existing);
    } else {
      const entry = { real, fake };
      cfg[kind].push(entry);
      added.push(entry);
    }
  }
  save(cfg);
  return { added, skipped };
}

export function removeItems(kind, inputs) {
  const spec = KINDS[kind];
  const cfg = load();
  const removed = [];
  for (const raw of inputs) {
    const real = spec.norm(raw);
    const i = cfg[kind].findIndex((e) => e.real === real);
    if (i >= 0) {
      removed.push(cfg[kind][i]);
      cfg[kind].splice(i, 1);
    }
  }
  save(cfg);
  return { removed };
}

export function regenerate(kind, inputs) {
  const spec = KINDS[kind];
  const cfg = load();
  const changed = [];
  const targets = inputs && inputs.length ? inputs.map(spec.norm) : cfg[kind].map((e) => e.real);
  for (const real of targets) {
    const entry = cfg[kind].find((e) => e.real === real);
    if (entry) {
      entry.fake = spec.fake(entry.real);
      changed.push(entry);
    }
  }
  save(cfg);
  return { changed };
}

export function phoneCanon(value) {
  const d = phoneDigits(value);
  return d.length >= 10 ? d.slice(-10) : d;
}

export function buildPairs(cfg) {
  const pairs = [];
  for (const e of cfg.emails) pairs.push([e.real, e.fake]);
  for (const p of cfg.phones) pairs.push([p.real, p.fake]);
  for (const t of cfg.customs || []) pairs.push([t.real, t.fake]);
  pairs.sort((a, b) => b[0].length - a[0].length);
  return pairs;
}

export function buildPayloadData(cfg) {
  const emails = cfg.emails
    .map((e) => [e.real, e.fake])
    .sort((a, b) => b[0].length - a[0].length);
  const phones = cfg.phones.map((p) => [phoneCanon(p.real), p.fake]);
  const customs = (cfg.customs || [])
    .map((t) => [t.real, t.fake])
    .sort((a, b) => b[0].length - a[0].length);
  return { emails, phones, customs };
}
