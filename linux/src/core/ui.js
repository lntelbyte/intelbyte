const useColor = process.stdout.isTTY && process.env.NO_COLOR === undefined;
const wrap = (code) => (s) => (useColor ? `\x1b[${code}m${s}\x1b[0m` : String(s));

const bold = wrap('1');
const plain = (s) => String(s);
const gray = wrap('90');

export const c = {
  bold,
  dim: wrap('2'),

  red: bold,
  green: bold,
  yellow: bold,
  blue: plain,
  magenta: bold,
  cyan: plain,
  gray,
};

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const ESC = '\x1b[';
export async function animateBanner(lines) {
  const animate =
    process.stdout.isTTY && process.env.NO_COLOR === undefined && !/^(dumb)$/.test(process.env.TERM || '');
  if (!animate) {
    for (const l of lines) console.log(useColor ? bold(l) : l);
    return;
  }
  const H = lines.length;
  const W = Math.max(...lines.map((l) => l.length));

  if (W >= (process.stdout.columns || 80)) {
    for (const l of lines) console.log(bold(l));
    return;
  }
  const band = 12;
  const step = 3;

  const frame = (pos) => {
    let out = '';
    for (const l of lines) {
      let s = '';
      for (let i = 0; i < l.length; i++) {
        const ch = l[i];
        if (ch === ' ') {
          s += ' ';
          continue;
        }
        const inBand = i <= pos && i > pos - band;
        s += `${ESC}${inBand ? '1' : '2'}m${ch}${ESC}0m`;
      }
      out += s + '\n';
    }
    return out;
  };

  process.stdout.write('\x1b[?25l');
  process.stdout.write(frame(-band));
  for (let pos = 0; pos <= W + band; pos += step) {
    process.stdout.write(`${ESC}${H}A\r`);
    process.stdout.write(frame(pos));
    await sleep(15);
  }
  process.stdout.write(`${ESC}${H}A\r`);
  let fin = '';
  for (const l of lines) fin += `${ESC}1m${l}${ESC}0m\n`;
  process.stdout.write(fin);
  process.stdout.write('\x1b[?25h');
}

export const ok = (s) => console.log(`${c.green('✔')} ${s}`);
export const info = (s) => console.log(`${c.cyan('ℹ')} ${s}`);
export const warn = (s) => console.log(`${c.yellow('⚠')} ${s}`);
export const err = (s) => console.error(`${c.red('✖')} ${s}`);
export const title = (s) => console.log(`\n${c.bold(c.magenta(s))}`);
export const line = (s = '') => console.log(s);

const BANNER = [
  '   ┬ ┌┐┌ ┌┬┐ ┌─┐ ┬   ┌┐ ┬ ┬ ┌┬┐ ┌─┐ ',
  '   │ │││  │  ├┤  │   ├┴┐└┬┘  │  ├┤  ',
  '   ┴ ┘└┘  ┴  └─┘ ┴─┘ └─┘ ┴   ┴  └─┘ ',
];
const BANNER_BIG = [
  '██╗███╗   ██╗████████╗███████╗██╗     ██████╗ ██╗   ██╗████████╗███████╗',
  '██║████╗  ██║╚══██╔══╝██╔════╝██║     ██╔══██╗╚██╗ ██╔╝╚══██╔══╝██╔════╝',
  '██║██╔██╗ ██║   ██║   █████╗  ██║     ██████╔╝ ╚████╔╝    ██║   █████╗  ',
  '██║██║╚██╗██║   ██║   ██╔══╝  ██║     ██╔══██╗  ╚██╔╝     ██║   ██╔══╝  ',
  '██║██║ ╚████║   ██║   ███████╗███████╗██████╔╝   ██║      ██║   ███████╗',
  '╚═╝╚═╝  ╚═══╝   ╚═╝   ╚══════╝╚══════╝╚═════╝    ╚═╝      ╚═╝   ╚══════╝',
];

export async function banner() {
  const cols = process.stdout.columns || 80;
  line('');
  const rows = cols >= 74 ? BANNER_BIG : BANNER;
  await animateBanner(rows);
  const tag = '🛡  screen-privacy shield · nothing leaks on stream';
  const pad = Math.max(0, Math.floor(((cols >= 74 ? 70 : 37) - tag.length) / 2));
  line(' '.repeat(pad) + c.gray(tag));
}
