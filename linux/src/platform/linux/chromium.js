import initSqlJs from 'sql.js';
import { existsSync, readFileSync, writeFileSync, readdirSync } from 'fs';
import { execFileSync } from 'child_process';
import { homedir } from 'os';
import { join, dirname } from 'path';
import { createRequire } from 'module';
import { phoneDigits } from '../../core/config.js';

const HOME = homedir();
const require = createRequire(import.meta.url);

const SQL_DIST = dirname(require.resolve('sql.js'));

const ROOTS = {
  chrome: {
    roots: [
      join(HOME, '.config/google-chrome'),
      join(HOME, '.var/app/com.google.Chrome/config/google-chrome'),
    ],

    procs: ['chrome', 'google-chrome', 'google-chrome-s'],
  },
  chromium: {
    roots: [
      join(HOME, '.config/chromium'),
      join(HOME, '.var/app/org.chromium.Chromium/config/chromium'),
    ],
    procs: ['chromium', 'chromium-browse'],
  },
  brave: {
    roots: [
      join(HOME, '.config/BraveSoftware/Brave-Browser'),
      join(HOME, '.var/app/com.brave.Browser/config/BraveSoftware/Brave-Browser'),
    ],
    procs: ['brave', 'brave-browser'],
  },
  edge: {
    roots: [
      join(HOME, '.config/microsoft-edge'),
      join(HOME, '.var/app/com.microsoft.Edge/config/microsoft-edge'),
    ],
    procs: ['msedge', 'microsoft-edge'],
  },
};

const DB_FILES = ['History', 'Web Data', 'Login Data', 'Shortcuts', 'Top Sites'];

let SQL = null;
async function sql() {
  if (!SQL) {
    SQL = await initSqlJs({ locateFile: (f) => join(SQL_DIST, f) });
  }
  return SQL;
}

export function installedChromiumBrowsers() {
  const out = [];
  for (const [name, spec] of Object.entries(ROOTS)) {
    if (spec.roots.some((r) => existsSync(r))) out.push(name);
  }
  return out;
}

export function isBrowserRunning(name) {
  const procs = (ROOTS[name] && ROOTS[name].procs) || [];
  for (const p of procs) {
    try {
      execFileSync('pgrep', ['-x', p], { stdio: 'ignore' });
      return true;
    } catch {}
  }
  return false;
}

function profileDirs(name) {
  const dirs = [];
  for (const root of (ROOTS[name] && ROOTS[name].roots) || []) {
    if (!existsSync(root)) continue;
    let kids;
    try {
      kids = readdirSync(root, { withFileTypes: true });
    } catch {
      continue;
    }
    for (const k of kids) {
      if (!k.isDirectory()) continue;
      const p = join(root, k.name);
      if (DB_FILES.some((f) => existsSync(join(p, f)))) dirs.push(p);
    }
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
          const stmt = db.prepare(
            `DELETE FROM ${q(table)} WHERE CAST(${q(col)} AS TEXT) LIKE :t`
          );
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
            const out = Buffer.from(db.export());
            writeFileSync(path, out);
          }
        } catch {} finally {
          db.close();
        }
        if (removed > 0) {
          scrubbed.push({ browser: name, profile: profileName(profile), db: file, removed });
        }
      }
    }
  }
  return { scrubbed, locked: [...locked] };
}

function profileName(dir) {
  const parts = dir.split('/');
  return parts[parts.length - 1];
}

export function chromiumProfileCount(name) {
  return profileDirs(name).length;
}
