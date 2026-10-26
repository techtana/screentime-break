# ScreenBreak

A small screen-time break reminder for Windows 10/11. It lives in the tray, uses
almost no memory, and needs nothing installed: it is built with the C# compiler
that ships with Windows (.NET Framework 4.8).

## What it does

- **Break popup that builds up.** After a set amount of screen time (default 60 min) a
  popup appears on **every monitor**. Over the ramp-up time (default 30 min) it fades in
  (15% → 90% opacity) and grows (30% → 100% of the screen area). Then the break starts.
- **You can still work behind it.** The popup is always on top, but clicks and keystrokes
  go straight through to the apps behind it, so you can save and close things.
- **Closes when the break is done.** The popup shows the countdown (default 10 min) and
  closes by itself. By default the countdown only runs while you're away from the
  keyboard, mouse and game controller. Being away for a full break length at any time
  also counts as a break.
- **Works over games** running in borderless/windowed fullscreen (most modern games on
  Windows 11). It re-asserts "on top" every second. Xbox-style controllers count as
  activity.
- **Pauses while you're away.** When there's been no input for a while (default 5 min),
  or you lock the PC, the countdown to the next break pauses and screen time isn't
  counted. It picks up again when you're back. Watching a video or using a full-screen
  app without touching anything doesn't count as away. All of this can be changed under
  **Away from the PC** in settings.
- **Background:** soft green (easy on the eyes), or your own image.
- **Skip button (optional):** a faint "skip" in the bottom-right corner of the popup on
  the main monitor. Click it twice to skip. Skips are counted. Skip more than once in 2 hours and a red
  "I'm very NOT happy" alert appears.
- **Daily limit (optional):** when screen time in the last 24 hours reaches the limit
  (default 4 h), an orange alert shows for a minute, repeating every 30 min.
- **Bedtime (optional):** starting 2 hours before bedtime, a small countdown sits in a
  corner of your choice. At bedtime a full-screen night popup stays up until wake-up time.
  The skip button snoozes it for 15 minutes, and that counts as a skip.

## Install

Run **`ScreenBreak-Setup.exe`** and click **Install**.

- It installs for your Windows account only, to `%LOCALAPPDATA%\Programs\ScreenBreak`,
  so it needs no admin rights.
- It adds a Start Menu shortcut. If you tick the boxes, it also adds a desktop shortcut and
  starts ScreenBreak with Windows. Both are off unless you choose them.
- Running a newer setup over an old install updates it and keeps your settings.
- To remove it: **Settings > Apps > Installed apps > ScreenBreak > Uninstall**. You'll be
  asked whether to also delete your settings and screen-time history.

The setup file isn't code-signed. If you download it from somewhere, Windows SmartScreen
may say "Windows protected your PC". Click **More info > Run anyway**.

Setup also runs from the command line, for scripted installs:

| Command | What it does |
|---|---|
| `ScreenBreak-Setup.exe /quiet` | Install silently. The app asks about starting with Windows on its first run |
| `ScreenBreak-Setup.exe /quiet /autostart` | Install silently and start with Windows |
| `uninstall.exe /uninstall /quiet` | Remove silently and keep your settings (run it from the install folder) |
| `uninstall.exe /uninstall /quiet /purge` | Remove silently and delete settings and history |

## Build

From PowerShell in this folder:

```powershell
.\build.ps1
```

This makes two files in `bin\`:

- `ScreenBreak.exe` is the app itself. It runs from anywhere, with no install needed.
- `ScreenBreak-Setup.exe` is the installer, with the app built in.

`.\build.ps1 -Icon` also redraws `assets\ScreenBreak.ico` from `tools\IconGen.cs`.

If PowerShell scripts are blocked on your PC, run them for this window only:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

To ship a new version, change the version numbers in `src\AssemblyInfo.cs` and
`installer\Setup.cs`, then rebuild.

## Use

- Open ScreenBreak from the Start Menu (or run `bin\ScreenBreak.exe`). It runs in the
  background with no window. Its green clock icon sits in the tray. On Windows 11 new
  icons start in the hidden-icons area under the **^** arrow; drag it onto the taskbar
  to keep it in view.
- **Click the icon** to open the status panel. It shows the time to the next break (or
  what's left of the current one), screen time in the last 24 hours against your limit,
  recent skips and bedtime. The **History** tab shows the last 7 days: screen time per
  day as a chart (with your daily limit as a dashed line), and breaks taken and skips per
  day. It has **Take a break now**, **Settings** and **Exit** buttons. It closes when you
  click elsewhere or press Esc.
- Opening ScreenBreak from the Start Menu while it's already running opens the same panel.
- **Right-click the icon** for a quick menu: status, take a break now, preview the popup,
  settings and exit.
- To try the popup right away, run `ScreenBreak.exe --preview` (20 s ramp, 15 s break).
  Settings also has an **Apply & preview** button.
- On first run it asks whether to start with Windows. You can change that any time with
  **Start ScreenBreak when I sign in to Windows** in settings.

Settings and logs are stored in `%APPDATA%\ScreenBreak\`:

| File | Contents |
|---|---|
| `settings.ini` | All settings (plain text, editable) |
| `usage.log` | One timestamp per active minute, last 24 h only |
| `skips.log` | One timestamp per skip, last 24 h only |
| `history.csv` | Daily totals for the History tab (screen time, breaks, skips), last 90 days |
| `error.log` | Details of unexpected errors, if any (capped at 256 KB) |

Nothing ever leaves your PC. The app has no network access. See [PRIVACY.md](PRIVACY.md).

## Good to know

- **Exclusive fullscreen games** (an older setting some games still offer) draw over
  every other window, including this one. Switch the game to *borderless* or
  *windowed fullscreen* and the popup works.
- A minute counts toward the daily limit unless you're away (see **Away from the PC** in
  settings: no input for 5 minutes by default, or the PC is locked). While a video plays
  or a full-screen app is in front you aren't counted as away, even without input; this
  uses the "keep the screen on" signal that video players and browsers send to Windows.
- Being away for a full break length counts as a break and resets the "time until next
  break".
- Exiting from the tray menu turns everything off. ScreenBreak is a nudge, not a lock.

## Publishing

Code signing, the Microsoft Store checklist and the security hardening are covered in
[docs/PUBLISHING.md](docs/PUBLISHING.md). `build.ps1 -CertThumbprint <thumbprint>` signs
both exes, and every build writes `bin\SHA256SUMS.txt`.
