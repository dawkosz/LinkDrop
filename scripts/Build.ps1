param([switch]$SelfContained, [switch]$IncludeTools, [string]$DotnetExe = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$publish = Join-Path $root 'dist\LinkDrop'
& $DotnetExe publish (Join-Path $root 'LinkDrop.csproj') -c Release -r win-x64 --self-contained $SelfContained.IsPresent.ToString().ToLowerInvariant() -p:DebugType=None -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
foreach ($name in 'README.md','README.pl.md','THIRD-PARTY.md','LICENSE','SetupTools.cmd','Start-LinkDrop.cmd') { Copy-Item (Join-Path $root $name) $publish -Force }
New-Item -ItemType Directory -Force (Join-Path $publish 'scripts') | Out-Null
Copy-Item (Join-Path $root 'scripts\Setup.ps1') (Join-Path $publish 'scripts') -Force
if ($IncludeTools) {
    if (!(Test-Path (Join-Path $root 'tools\yt-dlp.exe'))) { throw 'Run scripts\Setup.ps1 before building with -IncludeTools.' }
    New-Item -ItemType Directory -Force (Join-Path $publish 'tools') | Out-Null
    Copy-Item (Join-Path $root 'tools\*') (Join-Path $publish 'tools') -Recurse -Force
}
Write-Host "Ready: $publish\LinkDrop.exe"
