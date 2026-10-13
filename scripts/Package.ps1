$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'dist\LinkDrop'
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force $artifacts | Out-Null
$work = Join-Path ([IO.Path]::GetTempPath()) ('LinkDrop-package-' + [Guid]::NewGuid().ToString('N'))
$bundle = Join-Path $work 'LinkDrop'
New-Item -ItemType Directory -Path $bundle | Out-Null
try {
    foreach ($name in 'LinkDrop.exe','LinkDrop.dll','LinkDrop.deps.json','LinkDrop.runtimeconfig.json','README.md','README.pl.md','THIRD-PARTY.md','LICENSE','SetupTools.cmd','Start-LinkDrop.cmd') {
        $source = Join-Path $publish $name
        if (!(Test-Path $source)) { throw "Missing file: $name" }
        Copy-Item $source $bundle
    }
    Copy-Item (Join-Path $publish 'scripts') $bundle -Recurse
    # External engines and test reports are intentionally not redistributed.
    $zip = Join-Path $artifacts 'LinkDrop-1.2.1-windows-x64.zip'
    Compress-Archive -Path $bundle -DestinationPath $zip -Force
    "$((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($zip))" | Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Packaged: $zip"
} finally { Remove-Item -LiteralPath $work -Recurse -Force }
