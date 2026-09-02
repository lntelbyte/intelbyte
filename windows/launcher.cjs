// Tiny launcher packaged as IntelByte.exe — runs the real CLI via bundled Node.
const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');

const root = path.dirname(process.execPath);
const node = path.join(root, 'node', 'node.exe');
const app = path.join(root, 'app');
const bin = path.join(app, 'bin', 'intelbyte.js');

function die(msg) {
  console.error(msg);
  process.exit(1);
}

if (!fs.existsSync(node)) die('Missing node\\node.exe next to IntelByte.exe');
if (!fs.existsSync(bin)) die('Missing app\\bin\\intelbyte.js');

const child = spawn(node, [bin, ...process.argv.slice(2)], {
  stdio: 'inherit',
  windowsHide: false,
  cwd: app,
  env: {
    ...process.env,
    INTELBYTE_APP_ROOT: app,
    INTELBYTE_NODE: node,
  },
});

child.on('exit', (code) => process.exit(code ?? 0));
