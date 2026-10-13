# External tools

LinkDrop invokes these unmodified programs as separate processes. They are not included in this repository or its public app ZIP. `SetupTools.cmd` downloads them to the user's computer from the official release providers, with pinned SHA-256 checks.

| Tool | Version | Publisher and source | License |
| --- | --- | --- | --- |
| yt-dlp | 2026.08.19 | [yt-dlp release](https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19), [source](https://github.com/yt-dlp/yt-dlp/tree/2026.08.19) | Source is Unlicense; compiled Windows binary includes components under additional licenses. See [upstream notices](https://github.com/yt-dlp/yt-dlp#license). |
| FFmpeg and ffprobe | 9.0.2 essentials | [Gyan release](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2), [build information and source references](https://www.gyan.dev/ffmpeg/builds/), [FFmpeg source revision](https://github.com/FFmpeg/FFmpeg/commit/946fcce07b) | This Gyan essentials build is GPLv3 and includes separately licensed libraries. |
| Deno | 2.9.7 | [Deno release](https://github.com/denoland/deno/releases/tag/v2.9.7), [source](https://github.com/denoland/deno/tree/v2.9.7) | MIT with upstream third-party notices. |
| .NET and WPF | 9 | [Microsoft .NET](https://dotnet.microsoft.com/en-us/download/dotnet/9.0), [runtime source](https://github.com/dotnet/runtime), [WPF source](https://github.com/dotnet/wpf) | Microsoft distribution notices; open-source components are primarily MIT licensed. Public app ZIP uses the separately installed Desktop Runtime. |

The downloader scripts retain the available upstream license files beside the installed tools. Updating yt-dlp through the app changes that tool's version; inspect the new release notices if redistributing any engines yourself. The small public package intentionally contains only LinkDrop's own app files and setup scripts.
