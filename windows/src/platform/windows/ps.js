import { spawn, spawnSync } from 'child_process';

const PS = process.env.SystemRoot
  ? `${process.env.SystemRoot}\\System32\\WindowsPowerShell\\v1.0\\powershell.exe`
  : 'powershell.exe';

const BASE_ARGS = [
  '-NoProfile',
  '-NonInteractive',
  '-ExecutionPolicy',
  'Bypass',
  '-Command',
  '-',
];

export function ps(script) {
  const res = spawnSync(PS, BASE_ARGS, {
    input: script,
    encoding: 'utf8',
    maxBuffer: 32 * 1024 * 1024,
    windowsHide: true,
  });
  if (res.error) throw res.error;
  if (res.status !== 0) {
    throw new Error(`powershell exited ${res.status}: ${(res.stderr || '').trim()}`);
  }
  return (res.stdout || '').trim();
}

export function psJson(script) {
  const out = ps(script);
  if (!out) return null;
  try {
    return JSON.parse(out);
  } catch {
    return null;
  }
}

export function psAsync(script) {
  return new Promise((resolve, reject) => {
    const child = spawn(PS, BASE_ARGS, { windowsHide: true });
    let out = '';
    let errOut = '';
    child.stdout.on('data', (d) => (out += d));
    child.stderr.on('data', (d) => (errOut += d));
    child.on('error', reject);
    child.on('close', (code) => {
      if (code === 0) resolve(out.trim());
      else reject(new Error(`powershell exited ${code}: ${errOut.trim()}`));
    });
    child.stdin.end(script);
  });
}

export function psq(s) {
  return "'" + String(s).replace(/'/g, "''") + "'";
}
