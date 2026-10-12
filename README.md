# grokbot-background-helper

Keep the **Grok Bot desktop app** running in the background when you close its window, so the bot can keep reaching your computer (remote/local tools) while you're away. No app files are modified.

> **Disclaimer:** Unofficial community tool. Not affiliated with or endorsed by SpaceXAI / xAI. Use at your own risk.

## Why
On Windows, Grok Bot (Electron) quits when its last window closes (`window-all-closed` → `app.quit()` on non-darwin). There is no tray icon or "run in background" option. Patching `app.asar` is not viable: the ASAR-integrity and `OnlyLoadAppFromAsar` Electron fuses are enabled, so any change stops the app from launching.

## Windows (`windows/`)
How it works:
- A tiny WinForms tray app (C#, compiled with the built-in .NET Framework `csc.exe`; nothing to install).
- Per-monitor DPI aware (V2) so it works with real screen pixels at 125%/150%/... scaling (an early version broke at 150%).
- Every second it finds the app's main window (title + `Chrome_WidgetWin_1` class + process name) and locates the Close button by hit-testing the title bar with `WM_NCHITTEST` (`HTCLOSE`).
- A low-level mouse hook swallows mouse down/up on that rect; a keyboard hook swallows Alt+F4 when the app is foreground. Instead of closing, the window is hidden (`SW_HIDE`, also off the taskbar).
- Tray icon: single left-click (or **Show**) restores the window; right-click menu has **Quit <app>** (real quit) and **Exit helper**. Tooltip shows just the app name (`AppName` in `helper.ini`).
- Watchdog: if the main *window* is gone for ≥2 s and you didn't choose Quit, the app is relaunched and hidden. (It checks the window, not the process name — leftover child processes fooled a process-based check.)
- Logs to `helper.log` and `status.txt` next to the exe.

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
