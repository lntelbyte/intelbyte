import initSqlJs from 'sql.js';
import { existsSync, readFileSync, writeFileSync, readdirSync } from 'fs';
import { execFileSync } from 'child_process';
import { join, dirname } from 'path';
import { createRequire } from 'module';
import { phoneDigits } from '../../core/config.js';

const require = createRequire(import.meta.url);
const SQL_DIST = dirname(require.resolve('sql.js'));

function expand(p) {
  return p.replace(/%([^%]+)%/g, (_, v) => process.env[v] || '');
}

const ROOTS = {
  chrome: { root: '%LocalAppData%\\Google\\Chrome\\User Data', image: 'chrome.exe' },
  brave: { root: '%LocalAppData%\\BraveSoftware\\Brave-Browser\\User Data', image: 'brave.exe' },
  edge: { root: '%LocalAppData%\\Microsoft\\Edge\\User Data', image: 'msedge.exe' },
  chromium: { root: '%LocalAppData%\\Chromium\\User Data', image: 'chromium.exe' },
};

const DB_FILES = ['History', 'Web Data', 'Login Data', 'Shortcuts', 'Top Sites'];

let SQL = null;
async function sql() {
  if (!SQL) SQL = await initSqlJs({ locateFile: (f) => join(SQL_DIST, f) });
  return SQL;
}

function rootDir(name) {
  const r = ROOTS[name];
  return r ? expand(r.root) : null;
}

export function installedChromiumBrowsers() {
  const out = [];
  for (const name of Object.keys(ROOTS)) {
    const dir = rootDir(name);
    if (dir && existsSync(dir)) out.push(name);
  }
  return out;
}

export function isBrowserRunning(name) {
  const image = ROOTS[name] && ROOTS[name].image;
  if (!image) return false;
  try {
    const out = execFileSync('tasklist', ['/FI', `IMAGENAME eq ${image}`, '/NH'], {
      encoding: 'utf8',
      windowsHide: true,
    });
    return out.toLowerCase().includes(image.toLowerCase());
  } catch {
    return false;
  }
}

function profileDirs(name) {
  const dirs = [];
  const root = rootDir(name);
  if (!root || !existsSync(root)) return dirs;
  let kids;
  try {
    kids = readdirSync(root, { withFileTypes: true });
  } catch {
    return dirs;
  }
  for (const k of kids) {
    if (!k.isDirectory()) continue;
    const p = join(root, k.name);
    if (DB_FILES.some((f) => existsSync(join(p, f)))) dirs.push(p);
  }
  return dirs;
}

export function scrubTerms(cfg) {
  const terms = new Set();
  for (const e of cfg.emails || []) if (e.real) terms.add(e.real);
  for (const p of cfg.phones || []) {
    if (!p.real) continue;
    terms.add(p.real);
    const d = phoneDigits(p.real);
    if (d.length >= 7) {
      terms.add(d);
      if (d.length >= 10) terms.add(d.slice(-10));
    }
  }
  return [...terms].filter((t) => t.length >= 5);
}

const q = (id) => '"' + String(id).replace(/"/g, '""') + '"';

function scrubDb(db, terms) {
  let removed = 0;
  const tables = [];
  const res = db.exec("SELECT name FROM sqlite_master WHERE type='table'");
  if (res[0]) for (const [t] of res[0].values) tables.push(t);

  for (const table of tables) {
    if (table.startsWith('sqlite_')) continue;
    let cols;
    try {
      cols = db.exec(`PRAGMA table_info(${q(table)})`);
    } catch {
      continue;
    }
    if (!cols[0]) continue;
    const textCols = cols[0].values
      .filter(([, , type]) => !type || /CHAR|CLOB|TEXT|BLOB/i.test(type))
      .map(([, cname]) => cname);
    for (const col of textCols) {
      for (const term of terms) {
        try {
          const stmt = db.prepare(`DELETE FROM ${q(table)} WHERE CAST(${q(col)} AS TEXT) LIKE :t`);
          stmt.run({ ':t': `%${term}%` });
          stmt.free();
          removed += db.getRowsModified();
        } catch {}
      }
    }
  }
  return removed;
}

export async function scrubChromium(cfg, only = null) {
  const SQLmod = await sql();
  const terms = scrubTerms(cfg);
  const scrubbed = [];
  const locked = new Set();
  if (!terms.length) return { scrubbed, locked: [] };

  const names = only ? [only] : installedChromiumBrowsers();
  for (const name of names) {
    if (isBrowserRunning(name)) {
      locked.add(name);
      continue;
    }
    for (const profile of profileDirs(name)) {
      for (const file of DB_FILES) {
        const path = join(profile, file);
        if (!existsSync(path)) continue;
        let bytes;
        try {
          bytes = readFileSync(path);
        } catch {
          continue;
        }
        let db;
        try {
          db = new SQLmod.Database(bytes);
        } catch {
          continue;
        }
        let removed = 0;
        try {
          removed = scrubDb(db, terms);
          if (removed > 0) {
            db.run('VACUUM');
            writeFileSync(path, Buffer.from(db.export()));
          }
        } catch {} finally {
          db.close();
        }
        if (removed > 0) {
          scrubbed.push({ browser: name, profile: basenameOf(profile), db: file, removed });
        }
      }
    }
  }
  return { scrubbed, locked: [...locked] };
}

function basenameOf(dir) {
  const parts = dir.split(/[\\/]/);
  return parts[parts.length - 1];
}
