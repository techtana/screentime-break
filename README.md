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
- **Background:** soft green (easy on the eyes), or your own image.
- **Skip button (optional):** a faint "skip" in the bottom-right corner of the popup on
  the main monitor. Click it twice to skip. Skips are counted. Skip more than once in 2 hours and a red
  "I'm very NOT happy" alert appears.
- **Daily limit (optional):** when screen time in the last 24 hours reaches the limit
  (default 4 h), an orange alert shows for a minute, repeating every 30 min.
- **Bedtime (optional):** starting 2 hours before bedtime, a small countdown sits in a
  corner of your choice. At bedtime a full-screen night popup stays up until wake-up time.
  The skip button snoozes it for 15 minutes, and that counts as a skip.

## Build

From PowerShell in this folder:

```powershell
.\build.ps1
```

If scripts are blocked on your PC, run the compiler directly:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /out:bin\ScreenBreak.exe /win32manifest:src\app.manifest /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll (Get-ChildItem src\*.cs).FullName
```

The result is `bin\ScreenBreak.exe`, a single file you can copy anywhere.

## Use

- Run `bin\ScreenBreak.exe`. A green clock icon appears in the tray.
- Right-click the icon to see status, take a break now, preview the popup, or open settings.
- To try the popup right away, run `ScreenBreak.exe --preview` (20 s ramp, 15 s break).
  Settings also has an **Apply & preview** button.
- To start it automatically, tick **Start ScreenBreak when I sign in to Windows** in settings.

Settings and logs are stored in `%APPDATA%\ScreenBreak\`:

| File | Contents |
|---|---|
| `settings.ini` | All settings (plain text, editable) |
| `usage.log` | One timestamp per active minute, last 24 h only |
| `skips.log` | One timestamp per skip, last 24 h only |

## Good to know

- **Exclusive fullscreen games** (an older setting some games still offer) draw over
  every other window, including this one. Switch the game to *borderless* or
  *windowed fullscreen* and the popup works.
- A minute counts toward the daily limit if there was keyboard, mouse or controller input
  in the last 5 minutes. Watching a video without touching anything for 5+ minutes isn't
  counted. A gap longer than the break length (for example a 10 min video with no input)
  also resets the "time until next break".
- Exiting from the tray menu turns everything off. ScreenBreak is a nudge, not a lock.
