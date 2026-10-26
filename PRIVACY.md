# ScreenBreak privacy policy

_Last updated: 26 October 2026_

ScreenBreak is a screen-time break reminder for Windows. This policy explains what it
stores and what it does not do.

## What ScreenBreak stores

ScreenBreak keeps a small amount of data **only on your own PC**, in
`%APPDATA%\ScreenBreak\`:

| File | What's in it | How long it's kept |
|---|---|---|
| `settings.ini` | Your settings, such as break length and bedtime | Until you change or delete it |
| `usage.log` | One timestamp for each minute you were using the PC | Last 24 hours only |
| `skips.log` | One timestamp each time you skip a break | Last 24 hours only |
| `history.csv` | One line per day: minutes of screen time, breaks taken, breaks skipped | Last 90 days only |
| `background-*.jpg/png` | A copy of the background image you chose, if any | Until you choose another or uninstall |
| `error.log` | Technical details of unexpected errors, if any | Capped at 256 KB |

To tell whether you're using the PC, ScreenBreak checks **how long ago** the last
keyboard, mouse or game-controller input happened, whether the PC is locked, and whether
any app has asked Windows to keep the screen on (as video players do) or is running full
screen. It doesn't find out which app that is. It never records which keys you
press, what you click, what's on your screen, or which apps you use.

## What ScreenBreak does not do

- It does not connect to the internet or any server.
- It does not send, sell or share any data with anyone, Microsoft and the developer included.
- It has no accounts, ads, analytics or tracking.

## Your control over your data

- You can read or edit the files above at any time. They are plain text.
- Uninstalling offers to delete all of them. You can also delete the
  `%APPDATA%\ScreenBreak` folder yourself.

## Children

ScreenBreak collects no personal information from anyone, children included.

## Changes and contact

If a future version changes what is stored, this policy will be updated before that
version is released. Questions: **[add your contact email or website here]**.
