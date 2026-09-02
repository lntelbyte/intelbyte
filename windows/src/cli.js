import {
  load,
  save,
  addRandom,
  addCustom,
  removeItems,
  regenerate,
  buildPairs,
  configPath,
  maskValue,
  isPhone,
} from './core/config.js';
import { cfgFileName, prefFileName } from './core/firefoxui.js';
import { probePort } from './core/cdp.js';
import platform from './platform/index.js';
import { runShieldForeground } from './shield.js';
import { c, ok, info, warn, err, title, line, banner } from './core/ui.js';
import {
  runBackgroundWorker,
  cmdInstall,
  cmdUninstall,
  cmdStart,
  cmdStop,
  cmdRestart,
  cmdStatus,
  cmdPause,
  cmdResume,
  isRunning,
  isAutostartInstalled,
  binJs,
  nodeExe,
  trayScript,
} from './background.js';
import { spawn } from 'child_process';
import { join } from 'path';

const VERSION = '0.3.13';
const TRAY_SCRIPT = trayScript();

const CMD = { emails: 'protect-mail', phones: 'protect-phone', customs: 'protect-custom' };
const PLACEHOLDER = { emails: '<mail...>', phones: '<number...>', customs: '<text...>' };

async function usage() {
  await banner();
  line(`  ${c.gray('v' + VERSION)}  ${c.bold('hide your email & phone on screen')} ${c.gray('· OPSEC for streaming (Windows)')}`);
  line(c.gray('  Saved values are swapped for a fake in every CDP app — your real data stays intact.'));

  const groups = [
    [
      'WHAT TO HIDE',
      [
        ['protect-mail  <mail...>', 'Hide email(s) with a random fake'],
        ['protect-phone <number...>', 'Hide phone number(s) with a random fake'],
        ['protect-custom <text...>', 'Hide any text/name (all occurrences) with a random fake'],
        ['protect-mail  custom <real> <fake>', 'Pick the fake yourself (phone too)'],
        ['unprotect-mail / -phone / -custom <v>', 'Remove an entry'],
        ['list [--reveal]', 'Show entries → fakes (real values masked)'],
        ['regen [value...]', 'Regenerate fake(s)'],
      ],
    ],
    [
      'RUN IN THE BACKGROUND',
      [
        ['setup', 'One-time: wire every CDP app (browsers, Discord, Electron)'],
        ['install', 'Start the hidden background shield + auto-start at login'],
        ['tray', 'Show a system-tray icon to control the shield'],
        ['status', 'Is the background shield running? what is masked?'],
        ['start / stop / restart', 'Control the background shield'],
        ['pause / resume', 'Temporarily stop / resume masking'],
        ['uninstall', 'Stop it and remove the auto-start entry'],
      ],
    ],
    [
      'MORE',
      [
        ['shield', 'Run the shield in THIS window (foreground, for debugging)'],
        ['unsetup', 'Undo — restore all app shortcuts'],
        ['scrub [browser]', 'Purge your entries from Chromium history/autofill (address bar)'],
        ['firefox-ui-setup [--install]', 'EXPERIMENTAL: mask the Firefox address bar (admin)'],
        ['doctor', 'Environment check'],
        ['help', 'Show this help'],
      ],
    ],
  ];
  const w = Math.max(...groups.flatMap(([, rows]) => rows.map(([cmd]) => cmd.length)));
  for (const [heading, rows] of groups) {
    title(heading);
    for (const [cmd, desc] of rows) line('  ' + c.cyan(cmd.padEnd(w)) + '   ' + c.gray(desc));
  }

  title('QUICK START');
  const ex = [
    ['intelbyte protect-mail you@example.com', 'register what to hide'],
    ['intelbyte setup', 'once: wire the app shortcuts'],
    ['intelbyte install', 'run it hidden in the background, forever'],
  ];
  const ew = Math.max(...ex.map(([cmd]) => cmd.length));
  ex.forEach(([cmd, note], i) =>
    line('  ' + c.gray((i + 1) + '. ') + c.green(cmd.padEnd(ew)) + '   ' + c.gray('# ' + note))
  );

  line('');
  line(c.gray('  No extensions or mods — intelbyte attaches over CDP / WebDriver BiDi.'));
  line(c.gray('  Config: ' + configPath()));
  line('');
}

function activationHint() {
  line('');
  info(
    'Turn it on: ' +
      c.cyan('intelbyte setup') +
      c.gray(' then ') +
      c.cyan('intelbyte install') +
      c.gray('  (runs hidden in the background)')
  );
}

function reportAdd(added, skipped, custom, kind) {
  for (const e of added) {
    line(
      `${c.green('✔')} Added  ${c.gray(maskValue(e.real, kind))} ${c.gray('→')} ${c.green(e.fake)}` +
        (custom ? ' ' + c.gray('(custom)') : '')
    );
  }
  for (const s of skipped) warn(`Skipped  ${s.value} ${c.gray('(' + s.reason + ')')}`);
  if (added.length) activationHint();
}

function phoneQuoteHint(kind, skipped) {
  if (kind === 'phones' && skipped.length) {
    line(
      c.gray('  Tip: quote numbers that contain spaces → ') +
        c.cyan('intelbyte protect-phone "0532 123 45 67"')
    );
  }
}

function cmdProtect(kind, args) {
  if (args[0] === 'custom') {
    const rest = args.slice(1);
    if (rest.length < 2 || rest.length % 2 !== 0) {
      warn('Usage: ' + c.cyan(`intelbyte ${CMD[kind]} custom <real> <fake> [...]`));
      return;
    }
    const pairs = [];
    for (let i = 0; i < rest.length; i += 2) pairs.push([rest[i], rest[i + 1]]);
    const { added, skipped } = addCustom(kind, pairs);
    reportAdd(added, skipped, true, kind);
    phoneQuoteHint(kind, skipped);
    return;
  }
  if (!args.length) {
    warn('Give at least one value: ' + c.cyan(`intelbyte ${CMD[kind]} ${PLACEHOLDER[kind]}`));
    return;
  }
  let values = args;
  if (kind === 'phones' && args.length > 1 && isPhone(args.join(' '))) {
    values = [args.join(' ')];
  }
  const { added, skipped } = addRandom(kind, values);
  reportAdd(added, skipped, false, kind);
  phoneQuoteHint(kind, skipped);
}

function cmdProtectCustom(args) {
  const phrase = args.join(' ').trim();
  if (!phrase) {
    warn('What should I hide? ' + c.cyan('intelbyte protect-custom <text>'));
    return;
  }
  const { added, skipped } = addRandom('customs', [phrase]);
  reportAdd(added, skipped, false, 'customs');
}

function cmdProtectCustomExplicit(args) {
  if (args.length !== 2) {
    warn('Usage: ' + c.cyan('intelbyte protect-custom-custom <real> <fake>'));
    line(c.gray('  Quote multi-word terms: ') + c.cyan('intelbyte protect-custom-custom "ahmet yalçın" "mehmet demir"'));
    return;
  }
  const { added, skipped } = addCustom('customs', [[args[0], args[1]]]);
  reportAdd(added, skipped, true, 'customs');
}

function cmdUnprotect(kind, args) {
  if (!args.length) {
    warn('Which value should I remove?');
    return;
  }
  const { removed } = removeItems(kind, args);
  if (!removed.length) {
    warn('No matching entry found.');
    return;
  }
  for (const e of removed) ok(`Removed  ${maskValue(e.real, kind)}`);
}

function cmdRegen(args) {
  const changed = [
    ...regenerate('emails', args).changed,
    ...regenerate('phones', args).changed,
    ...regenerate('customs', args).changed,
  ];
  if (!changed.length) {
    warn('Nothing to regenerate.');
    return;
  }
  for (const e of changed) ok(`Regenerated  ${c.gray(maskValue(e.real))} ${c.gray('→')} ${c.green(e.fake)}`);
}

function printList(cfg, reveal) {
  const customs = cfg.customs || [];
  if (!cfg.emails.length && !cfg.phones.length && !customs.length) {
    info(
      'No protected entries yet. Add: ' +
        c.cyan('intelbyte protect-mail <mail>') +
        c.gray(' / ') +
        c.cyan('intelbyte protect-phone <number>') +
        c.gray(' / ') +
        c.cyan('intelbyte protect-custom <text>')
    );
    return;
  }
  const show = (real, kind) => (reveal ? real : maskValue(real, kind));
  if (cfg.emails.length) {
    title('Protected emails');
    for (const e of cfg.emails) line(`  ${c.gray(show(e.real, 'emails'))} ${c.gray('→')} ${c.green(e.fake)}`);
  }
  if (cfg.phones.length) {
    title('Protected phone numbers');
    for (const e of cfg.phones) line(`  ${c.gray(show(e.real, 'phones'))} ${c.gray('→')} ${c.green(e.fake)}`);
  }
  if (customs.length) {
    title('Protected custom text');
    for (const e of customs) line(`  ${c.gray(show(e.real, 'customs'))} ${c.gray('→')} ${c.green(e.fake)}`);
  }
  if (!reveal) {
    line(c.gray('\n  Real values masked — show full with: ') + c.cyan('intelbyte list --reveal'));
  }
  line(c.gray('\n  Config: ' + configPath()));
}

async function cmdDoctor() {
  title('intelbyte • environment check');
  ok(`Node ${process.version}`);

  const cfg = load();
  const nCustom = (cfg.customs || []).length;
  const n = cfg.emails.length + cfg.phones.length + nCustom;
  if (n)
    ok(
      `${cfg.emails.length} email(s), ${cfg.phones.length} phone(s)` +
        (nCustom ? `, ${nCustom} custom term(s)` : '') +
        ' protected'
    );
  else warn('No protected entries (add with protect-mail / protect-phone / protect-custom)');

  ok('Background shield: ' + (isRunning() ? c.green('running') : c.gray('stopped')) +
     c.gray('   auto-start: ') + (isAutostartInstalled() ? c.green('on') : c.gray('off')));

  const apps = cfg.apps || {};
  const ids = Object.keys(apps);
  if (!ids.length) {
    warn('No apps wired yet — run: ' + c.cyan('intelbyte setup'));
    return;
  }
  ok(`${ids.length} app(s) wired for auto-protection:`);
  for (const id of ids) {
    const a = apps[id];
    const live = await probePort(a.port);
    const state = live
      ? c.green('open — running protected')
      : (await platform.runningUnprotected(id, a))
        ? c.yellow('closed — RUNNING UNPROTECTED, restart it')
        : c.gray('closed — app not running');
    const bar = a.chromium
      ? (cfg.scrubAddressBar ? c.gray(' · addr-bar: scrub on launch') : c.gray(' · addr-bar: live mask'))
      : '';
    line(`  ${c.cyan(a.label.padEnd(16))} ${c.gray(`port ${a.port} · ${a.protocol}`)}  ${state}${bar}`);
  }

  const chromeBrowsers = platform.installedChromiumBrowsers();
  if (chromeBrowsers.length) {
    line('');
    for (const b of chromeBrowsers) {
      if (cfg.scrubAddressBar) {
        if (platform.isBrowserRunning(b)) {
          warn(`${b}: open now — address-bar data is only scrubbed when it's closed (` + c.cyan('intelbyte scrub ' + b) + ' after closing).');
        } else {
          ok(`${b}: closed — address-bar data scrubbed on next launch.`);
        }
      } else {
        info(`${b}: address bar masked live (history scrub off).`);
      }
    }
    if (cfg.scrubAddressBar) {
      info('Chromium address bar / autofill is native UI — intelbyte keeps it clean at the source, not by masking.');
    }
  }
  line(c.gray('\n  Background: ') + c.cyan('intelbyte install') + c.gray('   Re-scan apps: ') + c.cyan('intelbyte setup'));
}

async function cmdSetup() {
  title('intelbyte • setup — wire every CDP app for auto-protection');
  info('Discovering CDP/BiDi-capable apps (browsers, Discord, Electron) and their shortcuts…');
  const { apps, removedIds } = platform.setupApps();
  const ids = Object.keys(apps);
  if (!ids.length) {
    warn('No compatible app found. Install Discord / a Chromium browser / any Electron app and re-run.');
    return;
  }
  line('');
  const w = Math.max(...ids.map((id) => apps[id].label.length));
  for (const id of ids) {
    const a = apps[id];
    const scN = (a.shortcuts || []).length;
    const scNote = scN ? `${scN} shortcut${scN > 1 ? 's' : ''} rewired` : c.yellow('no shortcut — protected via relaunch');
    line(`  ${c.green('✔')} ${a.label.padEnd(w)}  ${c.gray(`port ${a.port} · ${a.protocol} · ${scNote}`)}`);
  }
  for (const id of removedIds) info(`Dropped stale app: ${id}`);
  line('');
  ok('App shortcuts now open with the debug port on. The background shield attaches automatically.');
  line(c.gray('  Browser apps started without the debug flag are reopened once automatically.'));
  line(c.gray('  Discord / Electron started without the flag are handled the same way.'));
  warn('Trade-off: an open debug port is a local attack surface (any local process could');
  line(c.gray('  drive the app). Undo anytime with: ') + c.cyan('intelbyte unsetup'));

  const cfg = load();
  if (cfg.scrubAddressBar && buildPairs(cfg).length && platform.installedChromiumBrowsers().length) {
    line('');
    const { scrubbed, locked } = await platform.scrubChromium(load());
    const totals = scrubbed.reduce((m, s) => ((m[s.browser] = (m[s.browser] || 0) + s.removed), m), {});
    for (const [b, num] of Object.entries(totals)) {
      ok(`Address-bar scrub: removed ${c.bold(num)} stored copy/ies from ${b}.`);
    }
    for (const b of locked) {
      warn(`${b} is open — close it and run ${c.cyan('intelbyte scrub ' + b)} to clean its address bar.`);
    }
    if (!scrubbed.length && !locked.length) info('Address-bar scrub: Chromium profiles already clean.');
  }

  const relaunched = await platform.relaunchUnprotectedApps();
  if (relaunched.length) {
    line('');
    for (const label of relaunched) ok(`${label} restarted with protection.`);
  }

  line('');
  ok(c.bold('Setup done.') + ' Run it in the background: ' + c.cyan('intelbyte install'));
}

async function cmdRelaunchApps() {
  const cfg = load();
  if (!Object.keys(cfg.apps || {}).length) {
    warn('No apps wired yet — run ' + c.cyan('intelbyte setup') + ' first.');
    return;
  }
  const relaunched = await platform.relaunchUnprotectedApps();
  if (!relaunched.length) info('No open apps needed a restart.');
  else for (const label of relaunched) ok(`${label} restarted with protection.`);
}

function cmdUnsetup() {
  const cfg = load();
  const { overrides } = platform.unwire(cfg.apps);
  cfg.apps = {};
  save(cfg);
  title('intelbyte • unsetup');
  if (overrides.length) {
    for (const p of overrides) ok(`Restored shortcut  ${c.gray(p)}`);
    info('App shortcuts launch normally again (no debug port). Already-open apps are unaffected.');
  } else {
    info('No shortcuts to restore.');
  }
}

async function cmdRunApp(args) {
  const [id, ...extra] = args;
  if (!id) {
    err('Usage: intelbyte run-app <id> [args…]');
    process.exitCode = 1;
    return;
  }
  if (args.includes('--dry')) {
    const argv = platform.runAppArgv(id, extra.filter((a) => a !== '--dry'));
    line(argv ? argv.join(' ') : `unknown app: ${id}`);
    return;
  }
  if (!(await platform.runApp(id, extra))) {
    err(`Unknown app: ${id} — re-run: intelbyte setup`);
    process.exitCode = 1;
  }
}

async function cmdScrub(args) {
  title('intelbyte • scrub Chromium address-bar data');
  const cfg = load();
  if (!buildPairs(cfg).length) {
    warn('Nothing to scrub for — add entries with protect-mail / protect-phone first.');
    return;
  }
  const installed = platform.installedChromiumBrowsers();
  if (!installed.length) {
    info('No Chromium browser profile found (Chrome/Chromium/Brave/Edge). Nothing to do.');
    return;
  }
  const only = args.find((a) => !a.startsWith('-'));
  const running = installed.filter((b) => (only ? b === only : true) && platform.isBrowserRunning(b));
  if (running.length) {
    warn(`Close ${running.join(', ')} first — its profile is locked while it runs; then re-run.`);
  }
  const { scrubbed, locked } = await platform.scrubChromium(cfg, only || null);
  if (!scrubbed.length && !locked.length) {
    ok('Already clean — no stored copies of your entries found.');
    return;
  }
  for (const s of scrubbed) {
    ok(`${s.browser} / ${s.profile} / ${s.db}: removed ${c.bold(s.removed)} row(s)`);
  }
  for (const b of locked) warn(`${b}: skipped (running) — close it and re-run to clean it.`);
  if (scrubbed.length) line(c.gray('  Your email/phone can no longer surface in these browsers’ address bar.'));
}

function cmdFirefoxUiSetup(args = []) {
  const doInstall = args.includes('--install') || args.includes('-y') || args.includes('install');
  const st = platform.firefox.stage();
  if (!st) {
    warn('Nothing to protect yet. Add with protect-mail / protect-phone first.');
    return;
  }
  title('intelbyte • Firefox address-bar masking  ' + c.yellow('(EXPERIMENTAL)'));
  warn('This patches your Firefox install (needs admin / UAC) and can break on Firefox updates.');
  line('');
  line('Generated, with your current entries baked in:');
  line(c.gray('  ' + st.cfgStage));
  line(c.gray('  ' + st.prefStage));
  line('');
  if (!st.appDir) {
    err('Firefox install dir not found. Copy the two files manually:');
    line(c.gray('  the .cfg  -> <firefox-dir>\\' + cfgFileName()));
    line(c.gray('  the .js   -> <firefox-dir>\\defaults\\pref\\' + prefFileName()));
    return;
  }
  if (doInstall) {
    info('Installing into ' + st.appDir + ' — Windows will ask for admin (UAC)…');
    line('');
    const okInstall = platform.firefox.install(st);
    line('');
    if (okInstall) {
      ok(c.bold('Installed.') + ' Now fully quit Firefox and reopen it — address-bar masking is live.');
      line(c.gray('  Remove: ') + c.cyan('intelbyte firefox-ui-remove'));
    } else {
      err('Install did not complete (UAC declined or errored). Copy the files yourself (elevated).');
    }
    return;
  }
  line(c.bold('Install:') + c.gray('  run ') + c.cyan('intelbyte firefox-ui-setup --install') + c.gray(' (triggers UAC), then restart Firefox.'));
  line(c.gray('Remove anytime: ') + c.cyan('intelbyte firefox-ui-remove'));
}

function cmdFirefoxUiRemove() {
  title('intelbyte • remove Firefox address-bar masking');
  const cmds = platform.firefox.removeCommands();
  if (!cmds) {
    warn('Firefox install dir not found; delete the installed cfg + pref files manually.');
    return;
  }
  line('Run these in an elevated PowerShell, then restart Firefox:');
  for (const cmd of cmds) line('   ' + c.cyan(cmd));
}

function cmdTray() {
  const psExe = join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
  try {
    const child = spawn(
      psExe,
      ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', TRAY_SCRIPT,
        '-NodeExe', nodeExe(), '-BinJs', binJs()],
      { detached: true, stdio: 'ignore', windowsHide: true }
    );
    child.unref();
    ok('Tray icon launched. Look for the intelbyte shield near the clock.');
    info('Right-click it for status, pause/resume, and quit.');
  } catch (e) {
    err('Could not launch the tray: ' + (e && e.message));
  }
}

async function cmdDefault() {
  const cfg = load();
  if (!buildPairs(cfg).length && !Object.keys(cfg.apps || {}).length) {
    await usage();
    return;
  }
  cmdStatus();
  line('');
  info('Foreground (debug) run: ' + c.cyan('intelbyte shield') + c.gray('   ·   full help: ') + c.cyan('intelbyte help'));
}

export async function run(argv) {
  const [cmd, ...args] = argv;
  switch (cmd) {
    case 'protect-mail':
    case 'add-mail':
      cmdProtect('emails', args);
      break;
    case 'protect-phone':
    case 'add-phone':
      cmdProtect('phones', args);
      break;
    case 'unprotect-mail':
      cmdUnprotect('emails', args);
      break;
    case 'unprotect-phone':
      cmdUnprotect('phones', args);
      break;
    case 'protect-custom':
    case 'add-custom':
      cmdProtectCustom(args);
      break;
    case 'protect-custom-custom':
      cmdProtectCustomExplicit(args);
      break;
    case 'unprotect-custom':
      cmdUnprotect('customs', [args.join(' ').trim()]);
      break;
    case 'list':
    case 'ls':
      printList(load(), args.includes('--reveal') || args.includes('-r') || args.includes('full'));
      break;
    case 'regen':
      cmdRegen(args);
      break;
    case 'setup':
      await cmdSetup();
      break;
    case 'relaunch-apps':
    case 'relaunch':
      await cmdRelaunchApps();
      break;
    case 'unsetup':
      cmdUnsetup();
      break;
    case 'run-app':
      await cmdRunApp(args);
      break;
    case 'scrub':
    case 'clean':
      await cmdScrub(args);
      break;

    case 'install':
      cmdInstall();
      break;
    case 'uninstall':
      cmdUninstall();
      break;
    case 'start':
      await cmdStart();
      break;
    case 'stop':
      cmdStop();
      break;
    case 'restart':
      await cmdRestart();
      break;
    case 'status':
      cmdStatus();
      break;
    case 'pause':
      cmdPause();
      break;
    case 'resume':
      cmdResume();
      break;
    case 'tray':
      cmdTray();
      break;
    case 'shield-bg':
      await runBackgroundWorker();
      break;

    case 'shield':
    case 'watch':
    case 'on':
      await runShieldForeground(platform, load());
      break;

    case 'firefox-ui-setup':
      cmdFirefoxUiSetup(args);
      break;
    case 'firefox-ui-remove':
      cmdFirefoxUiRemove();
      break;
    case 'doctor':
      await cmdDoctor();
      break;
    case 'version':
    case '--version':
    case '-v':
      line('intelbyte v' + VERSION + ' (Windows)');
      break;
    case 'help':
    case '--help':
    case '-h':
      await usage();
      break;
    case undefined:
      await cmdDefault();
      break;
    default:
      err(`Unknown command: ${cmd}`);
      await usage();
      process.exitCode = 1;
  }
}
