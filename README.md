# ZenFocus

[中文说明](README.zh-CN.md)

Windows desktop focus coach. Blocks distracting sites and apps on this PC, then shows where the time went. Data stays on the machine.

**$19 / ¥69 lifetime** (license key). Free already covers blocking + a focus timer.

## Download

[Latest release](https://github.com/yezhihua419-beep/ZenFocus/releases/latest) — get `ZenFocus-v0.9.4-win-x64.zip`.

1. Unzip and **keep the whole folder**. Do not copy only `ChanJing.App.exe`.
2. Right-click `ChanJing.App.exe` → **Run as administrator** (needed to write the hosts file).
3. Windows may warn about an unsigned app — choose **Run anyway**.

The UI defaults to English. You can switch to 中文 in settings.

## Limits (not bugs)

- Without administrator, browser tabs such as YouTube stay open. You only get a distraction bubble.
- Store / PWA windows minimize only when the window title matches the block list.
- There is no in-app card form. Chinese lifetime: [Afdian](https://afdian.com/a/zenfocus) — custom **¥69**, one time. Email us after payment for a key. USD checkout is not open yet.

## Free vs lifetime

Free is meant to solve the core job: block distractions and run a focus session. Lifetime unlocks extras.

| | Free | Lifetime ($19 / ¥69) |
|---|---|---|
| Site categories (6) + desktop app block | Yes | Yes |
| Focus timer, count-up, breath-in, deep / ADHD 15 min | Yes | Yes |
| Daily limits, allow 5/15/30 min, early-stop cooldown | Yes | Yes |
| Stats (today / 7-day / heatmap / quality) | Yes | Yes |
| Custom domains | 3 | Unlimited |
| Customize a scene (duration / categories / wish) | 1 scene | All 4 scenes |
| Phone companion (LAN QR) | — | Yes |
| Export stats (CSV / JSON) | — | Yes |
| ADHD buffer 20 / 30 min | — | Yes (free is 5 / 10 / 15) |

Gray-locked items stay clickable. They show a lock and an upgrade note. They are not disabled buttons, and they do not open an empty payment page.

## What it does

- **Block:** China / international catalogs (independent of UI language), custom domains, daily limits, temporary allow, manual shield, desktop apps (minimize or kill). Store PWAs are title-matched only.
- **Focus:** four scenes (Work / Write / Study / Meeting), count-up timer, one wish, breath-in, pause, early-stop cooldown, deep mode, ADHD short cycles.
- **Stats:** local minutes, streaks, 7-day / hour charts, share card, distraction sources.
- **System:** tray, dark mode, hotkeys `Ctrl+Alt+F` (toggle) · `P` (pause) · `R` (3 min break) · `S` (settings).
- **Companion (lifetime):** phone on the same Wi-Fi, QR token, remote start / early-stop.

Clicking a scene fills wish and duration only. The block-page checkboxes stay as you left them.

## Design

Count-up instead of a countdown. Easy exits (allow, step-away, early stop after cooldown). Coach copy talks about progress, not shame. Deep mode has no clock — you end it yourself.

## Privacy

- Local only: `%LOCALAPPDATA%\ChanJing\` (SQLite). No account, no cloud, no telemetry, no screenshots.
- Foreground window is process name + title, for matching and stats. No keylogging.
- Release builds do not write debug `actions.log`. If it crashes, send `crash.log` from that folder.

## Build from source

Windows, .NET 8 SDK, x64. Kill the running app first or the build cannot overwrite the exe.

```powershell
taskkill /F /IM ChanJing.App.exe
dotnet test tests\ChanJing.Tests\ChanJing.Tests.csproj
dotnet build src\ChanJing.App\ChanJing.App.csproj -p:Platform=x64
```

Self-contained folder (needs a local `license.secret` at the repo root — see `license.secret.example`):

```powershell
powershell -File tools\pack-inner.ps1
```

Output: `dist\ZenFocus-inner\`. Do not build the `.sln` without `-p:Platform=x64`.

## Contact

License / feedback: [yezhihua419@gmail.com](mailto:yezhihua419@gmail.com) · [yezhihua_yzh@163.com](mailto:yezhihua_yzh@163.com)
