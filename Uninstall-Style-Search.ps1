$ErrorActionPreference = 'Stop'
$programs = Join-Path $env:LOCALAPPDATA 'Programs'
$installDir = Join-Path $programs 'Style Search'
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$desktopDir = [Environment]::GetFolderPath('Desktop')
Get-Process AintoWindows, StyleSearch -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item (Join-Path $startMenuDir 'Style Search.lnk'), (Join-Path $desktopDir 'Style Search.lnk') -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name StyleSearch -ErrorAction SilentlyContinue
if (Test-Path $installDir) {
    $resolved = (Resolve-Path -LiteralPath $installDir).Path
    if ($resolved -eq $installDir) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
Write-Output 'Style Search was removed. Your settings and clipboard history were kept.'
