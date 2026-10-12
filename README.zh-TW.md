# grokbot-background-helper

讓 **Grok Bot 桌面版** 在你按 X 關閉視窗後繼續在背景執行，Bot 就能在你不在電腦前時繼續操作你的電腦。完全不修改 App 本身的檔案。

> **免責聲明：** 非官方社群工具，與 SpaceXAI／xAI 無關，也未獲其背書。使用風險自負。

## 為什麼需要
Windows 上 Grok Bot（Electron）關掉最後一個視窗就會結束程式，也沒有系統匣或「背景執行」選項；而 `app.asar` 開了完整性檢查等 Electron fuse，改檔就打不開，所以改用外掛小工具。

## Windows（`windows/`）
運作方式：
- 一個很小的系統匣程式（C#，用 Windows 內建 .NET Framework 的 `csc.exe` 編譯，不用另外安裝）。
- 支援每台螢幕各自的 DPI（Per-Monitor V2），150% 等縮放下用真實像素座標（第一版就是壞在 150%）。
- 每秒找到主視窗，用 `WM_NCHITTEST`（`HTCLOSE`）在標題列上探測出關閉按鈕的範圍。
- 全域滑鼠 hook 吞掉落在該範圍的按下／放開；鍵盤 hook 吞掉 Alt+F4。改成把視窗隱藏（`SW_HIDE`，工作列也不顯示）。
- 系統匣：左鍵點一下（或「Show」）叫回視窗；右鍵選單有「Quit」真正關閉 App、「Exit helper」結束小工具。滑鼠移上去只顯示 App 名稱（`helper.ini` 的 `AppName`）。
- 看門狗：主視窗消失 ≥2 秒且不是你按 Quit，就自動重開並隱藏。（看的是視窗而非程序名稱，因為殘留子程序曾騙過程序檢查。）
- 紀錄寫在 exe 旁的 `helper.log`、`status.txt`。

安裝（PowerShell）：
```powershell
cd windows
powershell -ExecutionPolicy Bypass -File .\build.ps1
powershell -ExecutionPolicy Bypass -File .\install.ps1     # 安裝到 %LOCALAPPDATA%\GrokBotTray 並加入開機啟動
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```
其他 Electron App：修改 `helper.ini`（`AppName`、`ExePath`、`WindowTitle`、`ProcessName`、`WindowClass`）。


## 限制
- Windows 依賴標題列 hit-test，App 介面改版可能要調整（看 `helper.log`）。全域 hook 可能被部分防毒軟體標記。只處理單一視窗。

## 授權
MIT
