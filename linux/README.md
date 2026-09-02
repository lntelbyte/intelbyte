# intelbyte (Linux)

Hide your email and phone number on screen while you stream or share your screen.

This is the Linux version. You run it in the terminal and keep it open while you
stream. On Windows, use the [Windows version](../windows) in this repo instead.
It runs in the background with a tray icon.

You tell intelbyte which emails, phone numbers, and names to hide. When you
screen-share or record, it shows a fake value in their place on screen. It works
in Discord, any Chromium browser (Chrome, Brave, Edge), Firefox, and any Electron
app (VS Code, Slack, Spotify, Signal, Obsidian, and so on). There are no browser
extensions, and nothing is changed inside any app.

The change is only visual, the same idea as editing a page with Inspect Element:
only the text you see changes. Your real data stays the same underneath, and
anything you type is left alone.

You set it up once. `intelbyte setup` finds every supported app and makes it
open with the debug port on from then on. After that you run `intelbyte`, and it
masks each app the moment you open it, however you open it.

Phone numbers are caught in any format. You add one number, for example
`+90 555 111 22 33`, and it is replaced on screen no matter how it shows up:
`5551112233`, `905551112233`, `+905551112233`, `(555) 111 2233`,
`0555 111 22 33`, and so on. It matches by the important digits and ignores the
country code, the leading `0`, and spaces or dashes. Normal numbers, dates, and
IDs are left alone.

It also handles the half-hidden form. When an app hides a phone but leaves the
last digits visible (Discord shows `***********6591`), those visible digits are
replaced with the fake's (`***********0000`), so nothing leaks even before you
click "Reveal".

You can also hide any text you want (a name, a handle, a tag) with
`protect-custom`.

**Note:** this hides your data on screen so it does not show up in a recording.
It is not encryption and not a security feature. The real data is still there
underneath.

## How it works

Discord, the Chromium browsers, and Electron apps are all built on Chromium. When
you start them with a debug port, programs can talk to them over the Chrome
DevTools Protocol (CDP). Firefox has a similar feature called WebDriver BiDi.

An app only accepts this connection if it was started with the debug port on, so
intelbyte works in two steps:

1. `intelbyte setup` (run once) looks at your installed apps and finds the
   supported ones: the Chromium browsers and Discord by name, and any other
   Electron app by checking for its `app.asar` file. It gives each one a fixed
   debug port and edits its launcher (in `~/.local/share/applications`) plus adds
   a small script on your PATH (in `~/.local/bin`). After that, however you open
   the app (menu, launcher, dock, a link, or typing its name), it opens with the
   debug port on.
2. `intelbyte` (the shield) watches those ports. The moment an app opens, it adds
   a small script to the page. The script replaces your protected values as text
   appears, and reaches into iframes and shadow DOM. It skips message boxes and
   inputs, so what you type is never changed.

Nothing is installed inside any app. The only things changed on disk are the
launcher edits and the helper scripts, and `unsetup` undoes them.

## Install

```bash
git clone https://github.com/lntelbyte/intelbyte
cd intelbyte/linux
npm install
npm link        # optional: makes `intelbyte` available everywhere
```

You need Node.js 18 or newer.

## Usage

```bash
# 1. add what you want to hide (a random fake is generated)
intelbyte protect-mail you@example.com
intelbyte protect-phone "+90 555 111 22 33"
intelbyte protect-custom "Your Name"

# ...or pick the fake yourself
intelbyte protect-mail custom you@example.com fake123@example.com

# 2. see everything and its fake
intelbyte list

# 3. one time: prepare every app so it opens with the debug port on
intelbyte setup

# 4. run the shield (keep it open while you stream)
intelbyte
```

After `setup`, any app you open, now or days later, is masked by the running
shield. If an app was already open before setup, it started without the debug
port, so close it fully and open it again once (`intelbyte doctor` points these
out). Keep the shield terminal open while you stream. `Ctrl+C` stops it (windows
that are already open stay masked until they reload).

You can type a spaced number directly: `intelbyte protect-phone 0532 123 45 67`.
The parts are joined back into one number. Only use quotes when adding several
numbers at once: `intelbyte protect-phone "0532 ..." "0555 ..."`.

## Commands

| Command | What it does |
|---|---|
| `protect-mail <mail...>` | Add email(s) with a random fake |
| `protect-mail custom <real> <fake>` | Add an email with a fake you choose |
| `protect-phone <number...>` | Add phone(s) with a random fake (same shape) |
| `protect-phone custom <real> <fake>` | Add a phone with a fake you choose |
| `protect-custom <text...>` | Hide any text or name with a matching fake |
| `protect-custom-custom <real> <fake>` | Add custom text with a fake you choose |
| `unprotect-mail` / `-phone` / `-custom <...>` | Remove an entry |
| `list` | Show your entries and their fakes (real values hidden) |
| `list --reveal` | Same, but show the real values in full |
| `regen [value...]` | Make new fake(s) |
| `setup` | Run once: prepare every app to open with the debug port on |
| `unsetup` | Undo the launcher edits and helper scripts |
| *(no command)* | Run the shield: mask every app whenever it opens |
| `scrub [browser]` | Remove your entries from Chromium history and autofill |
| `firefox-ui-setup [--install]` | Optional: mask the Firefox address bar (needs sudo) |
| `doctor` | Check your setup (Node, prepared apps, what is running) |
| `help` | Help |

Your settings (the values to hide and the list of prepared apps) live in
`~/.config/intelbyte/config.json`.

## The address bar

The text on a web page can be masked live. The browser's address bar and its
autofill popup are different: the browser draws them itself, and no script (CDP,
BiDi, or even an extension) can change them. intelbyte handles each browser in
its own way:

- **Chromium (Chrome, Brave, Edge):** intelbyte deletes your email and phone from
  the browser's own files (history, typed addresses, search terms, autofill), so
  there is nothing left to suggest. It runs when a prepared browser starts, during
  `setup`, and when you run `intelbyte scrub`. The browser locks these files while
  it runs, so close it first if it is open.
- **Firefox:** the suggestion text can be masked directly with a config file:
  `intelbyte firefox-ui-setup --install` (or it happens during `setup`).

The one thing neither can stop is you typing the value into the bar yourself on
stream. Those are your live keystrokes, not saved data.

## What it covers

- Works in Discord, Chrome, Brave, Edge, any Electron app, and Firefox.
- Covers pages inside other iframes and shadow DOM (like Google's account popup).
- The replacement is real page text in the app's own font, not an overlay.
- The address bar and autofill are handled at the source (see above), not by
  masking.

## What it does not do

- It does not cover normal (non-Electron) apps.
- A value inside a text box you can edit (like a "change email" field) is left
  alone on purpose.
- An app must be started with the debug port on. One that was already running
  before `setup` is not protected until you close and reopen it.
- While an app runs with the debug port on, any program on your machine could
  connect to it. `intelbyte unsetup` removes this.

## License

MIT
