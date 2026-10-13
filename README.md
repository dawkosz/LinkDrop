# LinkDrop

A Windows desktop app for downloading audio, video, playlists, and albums supported by yt-dlp. Built with C# and WPF, with a Polish interface, a persistent queue, grouped history, and local diagnostics.

**Version 1.2.1.** No account service or app telemetry. Downloads, thumbnails, and tool updates make normal network requests to their providers. Use the app for media you are entitled to download.

## Download and first run

1. Download the Windows ZIP from **Releases** and extract the entire folder.
2. Install the [.NET 9 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) if it is not already installed. Choose **Desktop Runtime**, not just the base runtime.
3. Double-click **Start-LinkDrop.cmd**. On first run it launches **SetupTools.cmd**, which downloads verified yt-dlp, FFmpeg/ffprobe, and Deno binaries directly from their official release providers. Internet access and roughly 350 MB of free space for the installed tools are needed.
4. Open `LinkDrop.exe` on subsequent runs; keep its `tools` subfolder alongside it.

The release ZIP contains the application, documentation, and setup scripts. It does **not** redistribute external media-engine binaries or include any user's download history. The EXE is unsigned and does not require administrator rights; installation of the .NET runtime may require elevation.

## Use

Paste an HTTP/HTTPS URL on **Home**, then choose **Sprawdź link**. Select audio/video, output format, quality, and destination folder.

- **Do kolejki** adds the job. The queue initially stays paused.
- **Pobierz teraz** starts the queue in order without interrupting the active job.
- Queue controls reorder/cancel jobs and pause the start of the next job; pausing does not stop the current file.
- History opens downloaded files/folders and retries failed, cancelled, or interrupted jobs.
- Closing during a download asks for confirmation. Saved jobs remain queued after restart and require a manual start.

## Formats and playlists

- Audio: MP3, Opus, M4A, or original audio. MP3/Opus offer 128, 192, 256, and 320 kb/s.
- Video: MP4/MKV, best available resolution or a selected limit after inspection.
- Higher bitrate or resolution settings cannot improve the source. A container choice does not guarantee a specific codec or player compatibility.
- Playlist/album URLs show entries for selection before queuing. Optional album subfolders are enabled by default. Unavailable entries are disabled, and individual failures do not stop later jobs.
- Up to 500 entries are displayed, with a visible limit notice for larger playlists. Entries outside this list are not silently downloaded.
- Each playlist addition has its own persistent group ID. Identically named additions remain separate. Group controls cancel remaining entries or retry unfinished ones while preserving completed files.
- File names start with the title rather than an added track-number prefix; track order remains stored internally.

Supported websites depend on yt-dlp. Login-protected and DRM content are outside this app's scope.

## Local data

Settings, queue, and history are in `%LOCALAPPDATA%\LinkDrop\state.json`, with an atomic backup `state.json.bak`. Incomplete fragments stay in `.linkdrop-temp` under the chosen output folder where resuming is supported.

Logs use `linkdrop.log` and one rotated copy, around 2 MB each. The UI keeps the latest 700 session entries. URL query strings are removed from logs, but titles, domains, and local paths can remain. History preserves full links for retries. Review these files before sharing them.

The Settings page can update yt-dlp when no download is active. To reinstall the pinned tool versions, run `SetupTools.cmd` with the application closed.

## Build

Windows 10/11 x64 and the .NET 9 SDK are required. The app does not use third-party NuGet packages.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
```

For local development with installed media tools:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Setup.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1 -IncludeTools
```

`Build.ps1 -SelfContained` also supports a local publish with its own .NET runtime. The public packaging script produces the small framework-dependent app package, excluding third-party engine binaries:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package.ps1
```

## Tests

After building with `-IncludeTools`, run the local integration suite:

```powershell
$p = Start-Process .\dist\LinkDrop\LinkDrop.exe -ArgumentList '--self-test', 'artifacts\self-test' -PassThru
$p.WaitForExit()
$p.ExitCode
```

It creates its own three-second FFmpeg fixture on a loopback HTTP server and exercises actual format conversions, codecs/bitrate, cancellation, queue behavior, grouping, URL validation, and persistent state. It uses an isolated test profile rather than your normal history. Optional `--ui-test` generates demonstration screens in a separate profile.

GitHub Actions builds the app, obtains verified tools for testing, runs the local suite, and packages the app without those external engines.

## License

LinkDrop's own code is MIT licensed. Media engines are invoked as separate processes and retain their own licenses; see [THIRD-PARTY.md](THIRD-PARTY.md). The setup script pins yt-dlp 2026.08.19, Gyan FFmpeg 9.0.2 essentials, and Deno 2.9.7 with SHA-256 verification. Future upstream changes may require updating these versions.

[Instrukcja po polsku](README.pl.md)
