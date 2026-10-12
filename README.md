# grokbot-background-helper

Keep the **Grok Bot desktop app** running in the background when you close its window, so the bot can keep reaching your computer (remote/local tools) while you're away. No app files are modified.

> **Disclaimer:** Unofficial community tool. Not affiliated with or endorsed by SpaceXAI / xAI. Use at your own risk.

## Why
On Windows, Grok Bot (Electron) quits when its last window closes (`window-all-closed` → `app.quit()` on non-darwin). There is no tray icon or "run in background" option. Patching `app.asar` is not viable: the ASAR-integrity and `OnlyLoadAppFromAsar` Electron fuses are enabled, so any change stops the app from launching.

## Windows (`windows/`)
How it works:
- A tiny WinForms tray app (C#, compiled with the built-in .NET Framework `csc.exe`; nothing to install).
- Per-monitor DPI aware (V2, then per-monitor, then system DPI) so hit-testing uses real screen pixels at 125%/150%/... scaling (an early version broke at 150%).
- Every second it finds the app's main window (title + `Chrome_WidgetWin_1` class + process name) and locates the Close button by hit-testing the title bar with `WM_NCHITTEST` (`HTCLOSE`). The probe follows that window's DPI, including a monitor above the primary. Coordinates that do not fit in `WM_NCHITTEST`'s 16-bit point use the caption-button metrics instead.
- A low-level mouse hook swallows mouse down/up on the cached close rectangle; a keyboard hook swallows Alt+F4 when the app is foreground (auto-repeat is swallowed too, and the window is hidden once per press). The hooks only read that rectangle and post the hide to the UI thread. If a hook is lost, the next tick reinstalls it. Instead of closing, the window is hidden (`SW_HIDE`, also off the taskbar).
- Tray icon: single left-click (or **Show**) restores the window; right-click menu has **Quit <app>** (real quit) and **Exit helper**. Tooltip shows just the app name (`AppName` in `helper.ini`).
- Watchdog: if the main window is gone for ≥2 s and you didn't choose Quit, the app is relaunched and hidden. Quit stays in effect while that window is still visible during shutdown, so a slow exit is not started again. A second copy is not started while the process this helper just launched is still alive, while a top-level `WindowClass` window of `ProcessName` exists, or while a `ProcessName` process is less than 20 s old. Older windowless leftover child processes do not block relaunch. A failed launch does not hide the next window you open.
- Logs to `helper.log` (renamed to `helper.log.old` past 256 KB) and `status.txt` next to the exe.

Install (PowerShell):
```powershell
cd windows
powershell -ExecutionPolicy Bypass -File .\build.ps1
powershell -ExecutionPolicy Bypass -File .\install.ps1     # copies to %LOCALAPPDATA%\GrokBotTray + Startup shortcut
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```
Other Electron apps: edit `helper.ini` (`AppName`, `ExePath`, `WindowTitle`, `ProcessName`, `WindowClass`). Default `ExePath` is `%LOCALAPPDATA%\Programs\Grok Bot\Grok Bot.exe`.


## Limitations
- Windows: relies on the title-bar hit-test; future UI changes may need adjusting (check `helper.log`). Global hooks may be flagged by some security tools. Only one window of the target app is handled.
- If the app updates and renames its exe/window title, update `helper.ini`.

## License
MIT
