#!/usr/bin/env node
const cmd = process.argv[2];
const light = new Set(['start', 'stop', 'status', 'restart', 'pause', 'resume', 'shield-bg']);

async function main() {
  if (light.has(cmd)) {
    const bg = await import('../src/background.js');
    if (cmd === 'shield-bg') await bg.runBackgroundWorker();
    else if (cmd === 'start') await bg.cmdStart();
    else if (cmd === 'stop') bg.cmdStop();
    else if (cmd === 'status') bg.cmdStatus();
    else if (cmd === 'restart') await bg.cmdRestart();
    else if (cmd === 'pause') bg.cmdPause();
    else if (cmd === 'resume') bg.cmdResume();
    return;
  }
  const { run } = await import('../src/cli.js');
  await run(process.argv.slice(2));
}

main().catch((e) => {
  console.error(e && e.stack ? e.stack : e);
  process.exit(1);
});
