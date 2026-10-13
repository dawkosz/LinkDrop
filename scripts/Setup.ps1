param([string]$ToolsDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$tools = if ($ToolsDirectory) { $ToolsDirectory } else { Join-Path $root 'tools' }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('LinkDrop-setup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tools,$scratch | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'
function Download-Verified($url, $path, $expected) {
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw "SHA-256 mismatch: $url" }
}
try {
    $yt = Join-Path $scratch 'yt-dlp.exe'
    Download-Verified 'https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp.exe' $yt '66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a'
    $ff = Join-Path $scratch 'ffmpeg.zip'
    Download-Verified 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip' $ff '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
    Expand-Archive -LiteralPath $ff -DestinationPath (Join-Path $scratch 'ffmpeg')
    $deno = Join-Path $scratch 'deno.zip'
    Download-Verified 'https://github.com/denoland/deno/releases/download/v2.9.7/deno-x86_64-pc-windows-msvc.zip' $deno 'a0c3101b4158d1dfb7d6a78a7bf0f3de80c96bb423c152beec8beb22786f2238'
    Expand-Archive -LiteralPath $deno -DestinationPath (Join-Path $scratch 'deno')
    Invoke-WebRequest 'https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.08.19/LICENSE' -OutFile (Join-Path $scratch 'YT-DLP-LICENSE.txt') -UseBasicParsing
    Invoke-WebRequest 'https://raw.githubusercontent.com/denoland/deno/v2.9.7/LICENSE.md' -OutFile (Join-Path $scratch 'DENO-LICENSE.txt') -UseBasicParsing
    Copy-Item $yt (Join-Path $tools 'yt-dlp.exe') -Force
    Get-ChildItem (Join-Path $scratch 'ffmpeg') -Recurse -Filter '*.exe' | Where-Object { $_.Name -in 'ffmpeg.exe','ffprobe.exe' } | Copy-Item -Destination $tools -Force
    Get-ChildItem (Join-Path $scratch 'ffmpeg') -Recurse -Filter 'LICENSE' | Select-Object -First 1 | Copy-Item -Destination (Join-Path $tools 'FFMPEG-LICENSE.txt') -Force
    Copy-Item (Join-Path $scratch 'deno\deno.exe') (Join-Path $tools 'deno.exe') -Force
    Copy-Item (Join-Path $scratch '*-LICENSE.txt') $tools -Force
    foreach ($name in 'yt-dlp.exe','ffmpeg.exe','ffprobe.exe','deno.exe') { if (!(Test-Path (Join-Path $tools $name))) { throw "Missing tool: $name" } }
    Write-Host "Tools installed in $tools"
} finally { Remove-Item -LiteralPath $scratch -Recurse -Force }
