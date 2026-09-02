import { mkdirSync, writeFileSync, readFileSync, existsSync } from 'fs';
import { join, dirname } from 'path';
import {
  buildMozillaCfg,
  buildAutoconfigPref,
  cfgFileName,
  prefFileName,
} from '../../core/firefoxui.js';
import { buildPayloadData, load, configDir } from '../../core/config.js';
import { ps, psq } from './ps.js';

function expand(p) {
  return p.replace(/%([^%]+)%/g, (_, v) => process.env[v] || '');
}

const FF_EXES = [
  '%ProgramFiles%\\Mozilla Firefox\\firefox.exe',
  '%ProgramFiles(x86)%\\Mozilla Firefox\\firefox.exe',
];

export function appDir() {
  for (const p of FF_EXES) {
    const full = expand(p);
    if (full && existsSync(full)) return dirname(full);
  }
  return null;
}

export function stage() {
  const data = buildPayloadData(load());
  if (!data.emails.length && !data.phones.length) return null;
  const dir = join(configDir(), 'firefox');
  mkdirSync(dir, { recursive: true });
  const cfgStage = join(dir, cfgFileName());
  const prefStage = join(dir, prefFileName());
  const cfgText = buildMozillaCfg(data);
  const prefText = buildAutoconfigPref();
  writeFileSync(cfgStage, cfgText);
  writeFileSync(prefStage, prefText);

  const dst = appDir();
  const cfgDest = dst ? join(dst, cfgFileName()) : null;
  const prefDir = dst ? join(dst, 'defaults', 'pref') : null;
  const prefDest = prefDir ? join(prefDir, prefFileName()) : null;

  let installed = false;
  let current = false;
  if (cfgDest && existsSync(cfgDest)) {
    installed = true;
    try {
      current =
        readFileSync(cfgDest, 'utf8') === cfgText &&
        !!prefDest &&
        existsSync(prefDest) &&
        readFileSync(prefDest, 'utf8') === prefText;
    } catch {
      current = false;
    }
  }
  return { appDir: dst, cfgStage, prefStage, cfgDest, prefDir, prefDest, installed, current, cfgText, prefText };
}

export function install(st) {
  if (!st.appDir) return false;
  const inner =
    `New-Item -ItemType Directory -Force -Path ${psq(st.prefDir)} | Out-Null; ` +
    `Copy-Item -LiteralPath ${psq(st.cfgStage)} -Destination ${psq(st.cfgDest)} -Force; ` +
    `Copy-Item -LiteralPath ${psq(st.prefStage)} -Destination ${psq(st.prefDest)} -Force`;
  try {
    ps(
      `Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden ` +
        `-ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-Command',${psq(inner)}`
    );
  } catch {
    return false;
  }
  try {
    return (
      existsSync(st.cfgDest) &&
      readFileSync(st.cfgDest, 'utf8') === st.cfgText &&
      existsSync(st.prefDest) &&
      readFileSync(st.prefDest, 'utf8') === st.prefText
    );
  } catch {
    return false;
  }
}

export function removeCommands() {
  const dst = appDir();
  if (!dst) return null;
  return [
    `Remove-Item -Force "${join(dst, cfgFileName())}"`,
    `Remove-Item -Force "${join(dst, 'defaults', 'pref', prefFileName())}"`,
  ];
}
