# intelbyte for Windows

Hide your email and phone number on screen while you stream or share your screen.

This is the Windows version. It runs quietly in the background, with an optional
tray icon, and starts by itself when you log in. On Linux, use the
[Linux version](../linux) in this repo instead. It runs as a terminal command.

You tell intelbyte which emails, phone numbers, and names to hide. When you
screen-share or record, it shows a fake value in their place on screen. It works
in Discord, any Chromium browser (Chrome, Brave, Edge), Firefox, and any Electron
app (VS Code, Slack, Spotify, Signal, Obsidian, and so on). There are no browser
extensions, and nothing is changed inside any app.

The change is only visual, the same idea as editing a page with Inspect Element:
only the text you see changes. Your real data stays the same underneath, and
anything you type is left alone.

Phone numbers are caught in any format. You add one number, for example
`+90 555 111 22 33`, and it is replaced no matter how it shows up: `5551112233`,
`905551112233`, `(555) 111 2233`, `0555 111 22 33`, and so on. It also handles
the half-hidden form (`***********6591` becomes `***********0000`). You can also
hide any text you want (a name, a handle) with a matching fake.

**Note:** this hides your data on screen so it does not show up in a recording.
It is not encryption and not a security feature. The real data is still there
underneath.

## How it works

Discord, the Chromium browsers, and Electron apps are all built on Chromium. When
you start them with a debug port, programs can talk to them over the Chrome
DevTools Protocol (CDP). Firefox has a similar feature called WebDriver BiDi.
intelbyte connects over that and adds a small script to the page. The script
replaces your protected values as text appears, and reaches into iframes and
shadow DOM. It skips message boxes and inputs, so what you type is never changed.

An app only accepts this connection if it was started with the debug port on. So
`intelbyte setup` does two things:

- It edits each app's shortcuts (Start Menu, Desktop, taskbar) to turn the debug
  port on, so opening them the normal way comes up ready to mask.
- The background shield watches each app. If it ever sees an app running without
  its debug port (started some other way, like a raw `.exe` or the Run box), it
  closes and reopens it with the debug port on. This is the real safety net; the
  shortcut edit just avoids a short flicker.

Nothing is installed inside any app. The only changes on disk are the shortcut
edits (undone by `unsetup`) and a startup entry (undone by `uninstall`).

## Install

### Option A — portable `.exe` (recommended)

No Node.js required.

1. Get `IntelByte-Windows.zip` from [Releases](https://github.com/lntelbyte/intelbyte/releases) or build it:

   ```powershell
   cd windows
   npm install
   npm run build:exe
   ```

2. Unzip and run commands from that folder:

   ```powershell
   .\IntelByte.exe protect-mail you@example.com
   .\IntelByte.exe setup
   .\IntelByte.exe install
   .\IntelByte.exe tray
   ```

The zip contains `IntelByte.exe`, a bundled `node\`, and the `app\` folder.

### Option B — from source (Node.js)

1. Install [Node.js 18 or newer](https://nodejs.org) (the LTS version is fine).
2. Download or clone this repo and install the dependencies:

   ```powershell
   git clone https://github.com/lntelbyte/intelbyte
   cd intelbyte\windows
   npm install
   npm link        # optional: makes `intelbyte` available in any terminal
   ```

   If you did not run `npm link`, use `node bin\intelbyte.js <command>` instead.

## Quick start

```powershell
# 1. add what to hide
intelbyte protect-mail you@example.com
intelbyte protect-phone "+90 555 111 22 33"
intelbyte protect-custom "Your Name"

# 2. one time: prepare every app's shortcuts
intelbyte setup

# 3. run it hidden in the background, and start it at every login
intelbyte install

# optional: a tray icon near the clock to control it
intelbyte tray
```

After `install`, it runs hidden and masks each app the moment you open it, now
and after every restart. Check on it any time:

```powershell
intelbyte status
```

## Commands

| Command | What it does |
|---|---|
| `protect-mail <mail...>` | Add email(s) with a random fake |
| `protect-phone <number...>` | Add phone(s) with a random fake (same shape) |
| `protect-custom <text...>` | Hide any text or name with a matching fake |
| `protect-mail custom <real> <fake>` | Add an email (or phone/custom) with a fake you choose |
| `unprotect-mail` / `-phone` / `-custom <...>` | Remove an entry |
| `list [--reveal]` | Show your entries and their fakes |
| `regen [value...]` | Make new fake(s) |
| `setup` | Run once: edit every app's shortcuts |
| `install` | Start the hidden background app and start it at login |
| `tray` | Show a tray icon to control it |
| `status` | Is it running? what is masked? |
| `start` / `stop` / `restart` | Control the background app |
| `pause` / `resume` | Turn masking off or on for a while |
| `uninstall` | Stop it and remove the startup entry |
| `shield` | Run it in this window instead (for testing) |
| `unsetup` | Put all the app shortcuts back |
| `scrub [browser]` | Remove your entries from Chromium history and autofill |
| `firefox-ui-setup [--install]` | Optional: mask the Firefox address bar (needs admin) |
| `doctor` | Check your setup |

Your settings live in `%APPDATA%\intelbyte\config.json`. The background app also
writes `status.json` and `shield.log` there.

## The tray icon

`intelbyte tray` shows an icon near the clock. Right-click it for:

- Status: running or paused, and how many apps are masked right now.
- Pause and resume masking.
- Restart the app.
- Open the settings folder or the log.
- Quit (stops the background app).

The tray and the commands stay in sync. They use the same files, so
`intelbyte pause` and the tray's Pause do the same thing.

## The address bar

The text on a page can be masked live. The browser's address bar and its
autofill popup are different: the browser draws them itself, and no script can
change them. So:

- **Chromium (Chrome, Brave, Edge):** intelbyte deletes your values from the
  browser's own files (history, typed addresses, search terms, autofill), so
  there is nothing to suggest. It runs before a prepared browser starts, during
  `setup`, and when you run `intelbyte scrub`. Close the browser first if it is
  open.

  ```powershell
  intelbyte scrub            # every installed Chromium browser (close it first)
  intelbyte scrub brave      # just one
  ```

- **Firefox:** the suggestion text can be masked with a config file:
  `intelbyte firefox-ui-setup --install` (it asks for admin; still experimental).

The one thing neither can stop is you typing the value into the bar yourself on
stream. Those are your live keystrokes, not saved data.

## What it covers

- Works in Discord, Chrome, Brave, Edge, any Electron app, and Firefox.
- Covers pages inside other iframes and shadow DOM.
- The replacement is real page text in the app's own font, not an overlay.

## What it does not do

- It does not cover normal (non-Electron) Windows apps.
- A value inside a text box you can edit (like a "change email" field) is left
  alone on purpose.
- A Windows shortcut only covers launches that go through it. If you start an app
  another way (a raw `.exe` or the Run box), the background app closes and reopens
  it to protect it, and you may lose the exact link or page you started with.
- The debug port only opens if the app is the first one to claim its profile. If
  Chrome or Discord is already open, the app closes and reopens it once.
- While an app runs with the debug port on, any program on your computer could
  connect to it. `unsetup` removes the shortcut edits, and `uninstall` removes
  the background app.

## License

MIT
