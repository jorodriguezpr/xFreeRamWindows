# Builds installer\out\xFreeRamWindows-Setup-<version>.exe
#   1. dotnet publish (self-contained win-x64, so users don't need .NET installed)
#   2. Inno Setup 6 compiler (ISCC.exe) with the version taken from the .csproj
# Requires Inno Setup 6: winget install JRSoftware.InnoSetup

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\xFreeRamWindows\xFreeRamWindows.csproj'
$outDir = Join-Path $PSScriptRoot 'out'
$publishDir = Join-Path $outDir 'publish'

[xml]$csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw "No <Version> in $project" }

$iscc = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found (winget install JRSoftware.InnoSetup).' }

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Write-Host "Publishing xFreeRamWindows $version ..."
dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Get-ChildItem $publishDir -Filter *.pdb | Remove-Item -Force

Write-Host 'Compiling installer ...'
& $iscc "/DAppVersion=$version" "/DPublishDir=$publishDir" (Join-Path $PSScriptRoot 'xFreeRamWindows.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }

$setup = Join-Path $outDir "xFreeRamWindows-Setup-$version.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
Write-Host "Done: $setup"
Write-Host "SHA-256: $hash"
