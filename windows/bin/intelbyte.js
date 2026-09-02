#!/usr/bin/env node
const cmd = process.argv[2];
const bgCmds = new Set(['start', 'stop', 'status', 'restart', 'pause', 'resume', 'shield-bg']);
const entryCmds = new Set([
  'list',
  'ls',
  'protect-mail',
  'add-mail',
  'protect-phone',
  'add-phone',
  'protect-custom',
  'protect-custom-custom',
  'unprotect-mail',
  'unprotect-phone',
  'unprotect-custom',
  'regen',
]);

async function main() {
  if (bgCmds.has(cmd)) {
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
  if (entryCmds.has(cmd)) {
    const { runEntries } = await import('../src/entries.js');
    runEntries(process.argv.slice(2));
    return;
  }
  const { run } = await import('../src/cli.js');
  await run(process.argv.slice(2));
}

main().catch((e) => {
  console.error(e && e.stack ? e.stack : e);
  process.exit(1);
});
