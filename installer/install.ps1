#Requires -RunAsAdministrator

$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:ProgramFiles 'xFreeRamWindows'
$sourceDir = $PSScriptRoot
$exeName = 'xFreeRamWindows.exe'

$runningProcess = Get-Process -Name 'xFreeRamWindows' -ErrorAction SilentlyContinue
if ($runningProcess) {
    Write-Host "xFreeRamWindows is currently running. Close it (tray icon -> Exit) before installing." -ForegroundColor Yellow
    exit 1
}

Write-Host "Installing xFreeRamWindows to $installDir ..."
New-Item -ItemType Directory -Path $installDir -Force | Out-Null

Get-ChildItem -Path $sourceDir -Exclude 'install.ps1', 'uninstall.ps1' | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $installDir -Recurse -Force
}

$exePath = Join-Path $installDir $exeName
if (-not (Test-Path $exePath)) {
    throw "Install failed: $exePath not found after copy."
}

$shell = New-Object -ComObject WScript.Shell

$startMenuDir = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'
$startMenuShortcut = $shell.CreateShortcut((Join-Path $startMenuDir 'xFreeRamWindows.lnk'))
$startMenuShortcut.TargetPath = $exePath
$startMenuShortcut.WorkingDirectory = $installDir
$startMenuShortcut.Description = 'xFreeRamWindows - free up RAM on demand'
$startMenuShortcut.Save()

$desktopShortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'xFreeRamWindows.lnk'))
$desktopShortcut.TargetPath = $exePath
$desktopShortcut.WorkingDirectory = $installDir
$desktopShortcut.Description = 'xFreeRamWindows - free up RAM on demand'
$desktopShortcut.Save()

Write-Host "Done. Shortcuts added to the Start Menu and Desktop." -ForegroundColor Green
Write-Host "Launch xFreeRamWindows from either shortcut - Windows will prompt for elevation (UAC), which is required."
