#Requires -RunAsAdministrator

param(
    [switch]$RemoveConfig
)

$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:ProgramFiles 'xFreeRamWindows'

$runningProcess = Get-Process -Name 'xFreeRamWindows' -ErrorAction SilentlyContinue
if ($runningProcess) {
    Write-Host "Stopping running xFreeRamWindows process..."
    $runningProcess | Stop-Process -Force
    Start-Sleep -Seconds 1
}

$taskName = 'xFreeRamWindows'
$taskExists = (schtasks /Query /TN $taskName 2>$null; $LASTEXITCODE -eq 0)
if ($taskExists) {
    Write-Host "Removing 'Start with Windows' scheduled task..."
    schtasks /Delete /TN $taskName /F | Out-Null
}

$startMenuShortcut = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\xFreeRamWindows.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'xFreeRamWindows.lnk'
Remove-Item -Path $startMenuShortcut, $desktopShortcut -Force -ErrorAction SilentlyContinue

if (Test-Path $installDir) {
    Write-Host "Removing $installDir ..."
    Remove-Item -Path $installDir -Recurse -Force
}

if ($RemoveConfig) {
    $configDir = Join-Path $env:APPDATA 'xFreeRamWindows'
    if (Test-Path $configDir) {
        Write-Host "Removing saved settings at $configDir ..."
        Remove-Item -Path $configDir -Recurse -Force
    }
}
else {
    Write-Host "Saved settings (service/process selections) were kept at `$env:APPDATA\xFreeRamWindows. Re-run with -RemoveConfig to also delete them."
}

Write-Host "xFreeRamWindows has been uninstalled." -ForegroundColor Green
