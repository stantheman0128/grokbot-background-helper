# Stops the helper, removes the Startup shortcut and the install folder. Grok Bot itself is untouched.
$dest = Join-Path $env:LOCALAPPDATA 'GrokBotTray'
Get-Process GrokBotTray -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item (Join-Path ([Environment]::GetFolderPath('Startup')) 'GrokBotTray.lnk') -ErrorAction SilentlyContinue
Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Uninstalled. If the app window was hidden, just launch it again.'
