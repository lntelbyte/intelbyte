# intelbyte

**Download (Windows):** [IntelByte-Setup.exe](https://github.com/lntelbyte/intelbyte/releases/latest/download/IntelByte-Setup.exe) — one file, no Node install.

Hide your email and phone number on screen while you stream or share your screen.

You tell intelbyte which emails, phone numbers, and names you want to hide. When
you screen-share or record, it shows a fake value in their place on screen. It
works in Discord, Chrome, Brave, Edge, Firefox, and any Electron app (VS Code,
Slack, Spotify, Signal, Obsidian, and so on). There are no browser extensions,
and nothing is changed inside any app.

The change is only visual. It is the same idea as editing a page with Inspect
Element: only the text you see changes. Your real data stays the same
underneath, and anything you type is left alone.

**Note:** this hides your data on screen so it does not show up in a recording.
It is not encryption and not a security feature. The real data is still there
underneath.

## Example

You add a value once. After that it shows up as a fake anywhere it would appear
on screen:

| You add | You still see | The recording shows |
|---|---|---|
| `you@example.com` | `you@example.com` | `k7f3qz@example.com` |
| `+90 555 111 22 33` | `+90 555 111 22 33` | `+90 312 908 44 61` |
| `Jane Doe` (a name) | `Jane Doe` | `Mkxw Rce` |

A few details:

- One phone number is caught in every format. You add `+90 555 111 22 33` once,
  and `5551112233`, `905551112233`, `(555) 111 2233`, and `0555 111 22 33` all
  turn into the same fake.
- It also handles the half-hidden form. When Discord shows `***********2233`, the
  visible last digits are replaced too (`***********4461`).
- Normal numbers, dates, and IDs are left alone.

## Two versions

Both versions share the same masking code. They only differ in how you run them.

- **Linux:** a command you run in the terminal and keep open while you stream.
- **Windows:** an app that runs in the background (with an optional tray icon)
  and starts by itself when you log in.

| | [Linux](./linux) | [Windows](./windows) |
|---|---|---|
| How you run it | A terminal command you keep open | A background app plus an optional tray icon; starts at login |
| How it sets up apps | Edits the app launchers and adds small helper scripts | Edits the app shortcuts (Start Menu, Desktop, taskbar) |
| Turn it on | `intelbyte setup`, then `intelbyte` | `intelbyte setup`, then `intelbyte install` |

Full docs: [linux/README.md](./linux/README.md) and
[windows/README.md](./windows/README.md).

## Quick start

**Linux**

```bash
cd linux
npm install
npm link                                   # optional: run `intelbyte` anywhere

intelbyte protect-mail you@example.com     # 1. add what to hide
intelbyte protect-phone "+90 555 111 22 33"
intelbyte protect-custom "Jane Doe"

intelbyte setup                            # 2. one time: prepare every app
intelbyte                                  # 3. run it while you stream
```

**Windows** (no Node install — portable `.exe`)

1. Download or build the release zip from the [`windows`](./windows) folder.
2. Unzip `IntelByte-Windows.zip`.
3. Double-click `IntelByte.exe`. If Windows Smart App Control / SmartScreen
   blocks it, that is because this build is not yet Authenticode-signed — not
   because the zip is broken. Right-click the exe → Properties → Unblock, then
   open it again. Do not turn off Smart App Control unless you trust this file.
4. Or run commands from that folder:

```powershell
.\IntelByte.exe protect-mail you@example.com
.\IntelByte.exe setup
.\IntelByte.exe install
```

Build the zip yourself: `cd windows` then `npm run build:exe`.

**Windows** (developers — Node.js installed)

```powershell
cd windows
npm install
npm link

intelbyte protect-mail you@example.com     # 1. add what to hide
intelbyte protect-phone "+90 555 111 22 33"
intelbyte protect-custom "Jane Doe"

intelbyte setup                            # 2. one time: prepare every app
intelbyte install                          # 3. run it hidden in the background
intelbyte tray                             # optional: tray icon to control it
```

You need [Node.js 18 or newer](https://nodejs.org).

## How it works

Discord, the Chromium browsers, and Electron apps are all built on Chromium.
When you start them with a debug port, other programs can talk to them over the
Chrome DevTools Protocol (CDP). Firefox has a similar feature called WebDriver
BiDi. intelbyte connects over that and adds a small script to the page. The
script watches the text and replaces your protected values as they appear, and
it reaches into iframes and shadow DOM too. It skips text boxes and inputs, so
what you type is never changed.

An app only accepts this connection if it was started with the debug port turned
on. So intelbyte does two things:

1. `setup` makes every future launch of the app turn the debug port on.
2. The running shield connects to each app and adds the script the moment the
   app opens.

## What it covers

- Works in Discord, Chrome, Brave, Edge, any Electron app, and Firefox.
- Covers pages inside other iframes and shadow DOM (like Google's account popup).
- The replacement is real page text in the app's own font, not an overlay on top.
- The browser address bar and autofill cannot be repainted by any script. For
  those, intelbyte removes your data from where the suggestions come from
  instead: it deletes it from the Chromium profile's history and autofill, and
  masks Firefox's suggestions with a small config file.

## What it does not do

- It does not cover normal (non-Electron) desktop apps. There is no clean way to
  rewrite their text.
- A value shown inside a text box you can edit (like a "change email" field) is
  left alone on purpose.
- An app that was already open before you turned protection on has no debug port
  yet. Close it and open it again once (the shield can also do this for you).
- While an app runs with the debug port on, any program on your computer could
  connect to it. `unsetup` removes this.

## Folder layout

```
intelbyte/
  linux/     Linux version (terminal command)
  windows/   Windows version (background app plus tray)
  README.md  this file
```

Each version is self-contained and has its own `package.json`, so you only
install and run the one for your system.

## License

MIT. Do what you want, no warranty.
