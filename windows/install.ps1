# Installs to %LOCALAPPDATA%\GrokBotTray, adds a Startup-folder shortcut, and starts the helper.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $env:LOCALAPPDATA 'GrokBotTray'
if (-not (Test-Path (Join-Path $here 'GrokBotTray.exe'))) { & (Join-Path $here 'build.ps1') }
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $here 'GrokBotTray.exe') $dest -Force
if (-not (Test-Path (Join-Path $dest 'helper.ini'))) { Copy-Item (Join-Path $here 'helper.ini') $dest }
$lnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'GrokBotTray.lnk'
$s = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
$s.TargetPath = Join-Path $dest 'GrokBotTray.exe'; $s.WorkingDirectory = $dest; $s.Save()
Start-Process (Join-Path $dest 'GrokBotTray.exe')
Write-Host "Installed to $dest; startup shortcut: $lnk"
