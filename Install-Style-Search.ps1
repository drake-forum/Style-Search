$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'StyleSearch-win-x64'
$programs = Join-Path $env:LOCALAPPDATA 'Programs'
$installDir = Join-Path $programs 'Style Search'
$oldInstall = Join-Path $programs 'AintoWindows'
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$desktopDir = [Environment]::GetFolderPath('Desktop')
$exe = Join-Path $installDir 'StyleSearch.exe'

if (-not (Test-Path (Join-Path $source 'StyleSearch.exe'))) { throw "Build folder not found: $source" }
Get-Process AintoWindows, StyleSearch -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force -Path $installDir, $startMenuDir | Out-Null
foreach ($fileName in @('StyleSearch.exe', 'logo.png', 'logo.ico')) {
    $from = Join-Path $source $fileName
    $to = Join-Path $installDir $fileName
    $copy = -not (Test-Path $to)
    if (-not $copy) {
        $copy = (Get-FileHash -LiteralPath $from).Hash -ne (Get-FileHash -LiteralPath $to).Hash
    }
    if ($copy) { Copy-Item -LiteralPath $from -Destination $to -Force }
}

$shell = New-Object -ComObject WScript.Shell
foreach ($shortcutPath in @((Join-Path $startMenuDir 'Style Search.lnk'), (Join-Path $desktopDir 'Style Search.lnk'))) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = 'Style Search launcher'
    $shortcut.Save()
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKey -Force | Out-Null
Remove-ItemProperty -Path $runKey -Name AintoWindows -ErrorAction SilentlyContinue
Set-ItemProperty -Path $runKey -Name StyleSearch -Value "`"$exe`""
Remove-Item (Join-Path $startMenuDir 'Ainto Windows.lnk'), (Join-Path $desktopDir 'Ainto Windows.lnk') -Force -ErrorAction SilentlyContinue

if (Test-Path $oldInstall) {
    $oldResolved = (Resolve-Path -LiteralPath $oldInstall).Path
    if ($oldResolved -eq $oldInstall) { Remove-Item -LiteralPath $oldResolved -Recurse -Force }
}
Write-Output "Installed Style Search to $installDir"
Write-Output 'Added Start Menu and Desktop shortcuts and enabled launch at sign-in.'
