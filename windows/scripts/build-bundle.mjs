import * as esbuild from 'esbuild';
import { mkdirSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const outdir = join(root, 'dist', 'bundle');

mkdirSync(outdir, { recursive: true });

await esbuild.build({
  entryPoints: [join(root, 'bin', 'intelbyte.js')],
  bundle: true,
  platform: 'node',
  target: 'node18',
  format: 'cjs',
  outfile: join(outdir, 'intelbyte.cjs'),
  banner: {
    js: "const require = (await import('node:module')).createRequire(import.meta.url);",
  },
  external: [],
  logLevel: 'info',
});

console.log('Bundled ->', join(outdir, 'intelbyte.cjs'));
