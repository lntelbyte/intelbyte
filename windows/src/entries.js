import {
  load,
  addRandom,
  addCustom,
  removeItems,
  regenerate,
  configPath,
  maskValue,
  isPhone,
} from './core/config.js';
import { c, ok, info, warn, title, line } from './core/ui.js';

const CMD = { emails: 'protect-mail', phones: 'protect-phone', customs: 'protect-custom' };
const PLACEHOLDER = { emails: '<mail...>', phones: '<number...>', customs: '<text...>' };

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

function reportAdd(added, skipped, custom, kind) {
  for (const e of added) {
    line(
      `${c.green('✔')} Added  ${c.gray(maskValue(e.real, kind))} ${c.gray('→')} ${c.green(e.fake)}` +
        (custom ? ' ' + c.gray('(custom)') : '')
    );
  }
  for (const s of skipped) warn(`Skipped  ${s.value} ${c.gray('(' + s.reason + ')')}`);
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

export function runEntries(argv) {
  const [cmd, ...args] = argv;
  switch (cmd) {
    case 'list':
    case 'ls':
      printList(load(), args.includes('--reveal') || args.includes('-r') || args.includes('full'));
      break;
    case 'protect-mail':
    case 'add-mail':
      cmdProtect('emails', args);
      break;
    case 'protect-phone':
    case 'add-phone':
      cmdProtect('phones', args);
      break;
    case 'protect-custom':
      cmdProtectCustom(args);
      break;
    case 'protect-custom-custom':
      cmdProtectCustomExplicit(args);
      break;
    case 'unprotect-mail':
      cmdUnprotect('emails', args);
      break;
    case 'unprotect-phone':
      cmdUnprotect('phones', args);
      break;
    case 'unprotect-custom':
      cmdUnprotect('customs', [args.join(' ').trim()]);
      break;
    case 'regen':
      cmdRegen(args);
      break;
    default:
      return false;
  }
  return true;
}
