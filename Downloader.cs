using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LinkDrop;

public sealed class Downloader
{
    public string ToolsDirectory { get; }
    public Downloader(string? tools = null) => ToolsDirectory = tools ?? Path.Combine(AppContext.BaseDirectory, "tools");
    public bool IsReady => new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe" }.All(f => File.Exists(Path.Combine(ToolsDirectory, f)));
    public static bool IsValidUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
    public static string SafeLog(string line)
    {
        line = Regex.Replace(line, @"https?://[^\s""<>]+", m => Uri.TryCreate(m.Value, UriKind.Absolute, out var u) ? u.GetLeftPart(UriPartial.Path) : "[link]");
        return line.Length > 3000 ? line[..3000] + "…" : line;
    }
    private List<string> BaseArguments() => ["--ignore-config", "--no-playlist", "--no-colors", "--encoding", "utf-8", "--socket-timeout", "20", "--retries", "3", "--ffmpeg-location", ToolsDirectory, "--js-runtimes", "deno:" + Path.Combine(ToolsDirectory, "deno.exe")];
    public List<string> BuildArguments(Job job, bool debug = false)
    {
        if (!IsValidUrl(job.Url)) throw new ArgumentException("Wklej poprawny link HTTP lub HTTPS.");
        var args = BaseArguments();
        args.AddRange(["--newline", "--progress", "--progress-delta", "0.3", "--windows-filenames", "--trim-filenames", "150", "--no-overwrites", "--no-mtime", "--paths", job.Folder, "--paths", "temp:" + Path.Combine(job.Folder, ".linkdrop-temp"), "-o", "%(title)s [%(id)s]-" + job.Id[..8] + ".%(ext)s", "--progress-template", "download:LD_PROGRESS:%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s", "--print", "after_move:LD_FILE:%(filepath)s"]);
        if (debug) args.Add("--verbose");
        if (job.Mode == "Audio")
        {
            args.AddRange(["-f", "bestaudio/best", "-x"]);
            if (job.Format != "Oryginał")
            {
                args.AddRange(["-x", "--audio-format", job.Format.ToLowerInvariant()]);
                if (job.Format is "MP3" or "Opus") args.AddRange(["--audio-quality", job.Quality.Split(' ')[0] + "K"]);
                else args.AddRange(["--audio-quality", "0"]);
            }
        }
        else
        {
            string cap = job.Quality == "Najlepsza dostępna" ? "" : "[height<=" + job.Quality.TrimEnd('p') + "]";
            string selector = job.Format == "MP4"
                ? $"bv*[ext=mp4]{cap}+ba[ext=m4a]/b[ext=mp4]{cap}/bv*{cap}+ba/b{cap}"
                : $"bv*{cap}+ba/b{cap}";
            args.AddRange(["-f", selector, "--merge-output-format", job.Format.ToLowerInvariant(), "--remux-video", job.Format.ToLowerInvariant()]);
        }
        args.AddRange(["--", job.Url]);
        return args;
    }
    public static TransferProgress? ParseProgress(string line)
    {
        if (!line.StartsWith("LD_PROGRESS:")) return null;
        var parts = line[12..].Split('|');
        if (parts.Length != 3 || !double.TryParse(parts[0].Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double percent)) return null;
        return new(Math.Clamp(percent, 0, 100), $"{parts[1].Trim()}  ·  pozostało {parts[2].Trim()}");
    }
    public async Task<MediaInfo> Inspect(string url, CancellationToken token, Action<string> log)
    {
        if (!IsValidUrl(url)) throw new ArgumentException("Wklej poprawny link HTTP lub HTTPS.");
        var args = InspectionArguments(url);
        var json = new StringBuilder(); var errors = new StringBuilder();
        int exit = await Run(Path.Combine(ToolsDirectory, "yt-dlp.exe"), args, token, line => json.AppendLine(line), line => { errors.AppendLine(line); log(SafeLog(line)); });
        if (exit != 0) throw new InvalidOperationException(FriendlyError(errors.ToString()));
        if (string.IsNullOrWhiteSpace(json.ToString())) throw new InvalidOperationException(FriendlyError(errors.ToString()));
        return ParseInspection(json.ToString());
    }
    public List<string> InspectionArguments(string url)
    {
        var args = BaseArguments(); args.Remove("--no-playlist");
        args.AddRange(["--yes-playlist", "--flat-playlist", "--ignore-errors", "--playlist-end", "501", "--dump-single-json", "--skip-download", "--", url]);
        return args;
    }
    private static string Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    public static string MediaKey(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        bool youtube = uri.Host is "youtube.com" or "www.youtube.com" or "music.youtube.com" or "m.youtube.com" or "youtu.be";
        string id = uri.Host == "youtu.be" ? uri.AbsolutePath.Trim('/') : Regex.Match(uri.Query, @"(?:\?|&)v=([^&]+)").Groups[1].Value;
        return youtube && id.Length > 0 ? "youtube:" + id : uri.AbsoluteUri;
    }
    public static string PlaylistFolder(string title)
    {
        string name = Regex.Replace(title, "[<>:\"/\\\\|?*%\\x00-\\x1F]", "_").Trim().TrimEnd('.');
        if (name.Length > 80) name = name[..80].TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(name)) name = "Playlista";
        if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) name = "_" + name;
        return name;
    }
    public static MediaInfo ParseInspection(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        string Get(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            var items = new List<PlaylistEntry>(); int index = 0;
            foreach (var entry in entries.EnumerateArray().Take(500))
            {
                index++;
                string link = Text(entry, "webpage_url");
                if (!IsValidUrl(link)) link = Text(entry, "url");
                bool youtube = Text(entry, "ie_key").Equals("Youtube", StringComparison.OrdinalIgnoreCase) || Get("extractor").StartsWith("youtube", StringComparison.OrdinalIgnoreCase);
                if (!IsValidUrl(link) && youtube && Regex.IsMatch(Text(entry, "id"), @"^[A-Za-z0-9_-]{11}$")) link = "https://www.youtube.com/watch?v=" + Text(entry, "id");
                string title = Text(entry, "title");
                bool available = IsValidUrl(link) && Text(entry, "availability") != "private" && title is not ("[Private video]" or "[Deleted video]") && Text(entry, "_type") != "playlist";
                int number = entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("playlist_index", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out int value) ? value : index;
                items.Add(new PlaylistEntry { Number = number, Title = string.IsNullOrWhiteSpace(title) ? "Pozycja " + number : title, Url = link, Available = available, Selected = available, Detail = available ? Text(entry, "uploader") : "Niedostępne lub brak bezpośredniego linku" });
            }
            return new(Get("title"), Get("uploader"), $"{items.Count} pozycji", Get("thumbnail"), [], items, entries.GetArrayLength() > 500);
        }
        double duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetDouble(out var seconds) ? seconds : 0;
        var heights = new HashSet<int>();
        if (root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array) foreach (var format in formats.EnumerateArray()) if (format.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out int n)) heights.Add(n);
        return new(Get("title"), Get("uploader"), duration > 0 ? TimeSpan.FromSeconds(duration).ToString(duration >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "Czas nieznany", Get("thumbnail"), heights.OrderDescending().ToArray());
    }
    public async Task<string> Download(Job job, bool debug, CancellationToken token, Action<TransferProgress> progress, Action<string> log, Action processing)
    {
        Directory.CreateDirectory(job.Folder);
        string output = ""; var errors = new StringBuilder();
        int exit = await Run(Path.Combine(ToolsDirectory, "yt-dlp.exe"), BuildArguments(job, debug), token, line =>
        {
            var p = ParseProgress(line);
            if (p != null) { progress(p); return; }
            if (line.StartsWith("LD_FILE:")) { output = line[8..].Trim(); return; }
            if (line.StartsWith("[ExtractAudio]") || line.StartsWith("[Merger]") || line.StartsWith("[VideoRemuxer]")) processing();
            log(SafeLog(line));
        }, line => { errors.AppendLine(line); log(SafeLog(line)); });
        token.ThrowIfCancellationRequested();
        if (exit != 0) throw new InvalidOperationException(FriendlyError(errors.ToString()));
        if (string.IsNullOrWhiteSpace(output) || !File.Exists(output)) throw new IOException("Proces zakończył się bez pliku wynikowego. Zajrzyj do logów.");
        return output;
    }
    public async Task Update(CancellationToken token, Action<string> log)
    {
        int exit = await Run(Path.Combine(ToolsDirectory, "yt-dlp.exe"), ["--ignore-config", "-U"], token, log, log);
        if (exit != 0) throw new IOException("Aktualizacja nie powiodła się. Szczegóły w logach.");
    }
    public static string FriendlyError(string errors)
    {
        if (errors.Contains("[DRM]", StringComparison.OrdinalIgnoreCase)) return "yt-dlp nie obsługuje tego źródła ze względu na oznaczenie DRM. Dotyczy to także albumów i playlist z tego serwisu.";
        if (errors.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase)) return "Ten serwis lub rodzaj linku nie jest obsługiwany.";
        if (errors.Contains("Sign in", StringComparison.OrdinalIgnoreCase) || errors.Contains("login", StringComparison.OrdinalIgnoreCase)) return "Serwis wymaga logowania lub dodatkowej weryfikacji. Ten materiał nie jest obecnie dostępny.";
        if (errors.Contains("Requested format", StringComparison.OrdinalIgnoreCase)) return "Wybrana jakość jest niedostępna. Wybierz inną jakość i dodaj materiał ponownie.";
        if (errors.Contains("403")) return "Serwis odmówił dostępu (403). Spróbuj zaktualizować yt-dlp w ustawieniach.";
        return SafeLog(errors.Split('\n').LastOrDefault(l => l.Contains("ERROR"))?.Trim() ?? "Pobieranie nie powiodło się. Sprawdź połączenie i szczegóły w logach.");
    }
    public static async Task<int> Run(string executable, IEnumerable<string> arguments, CancellationToken token, Action<string> stdout, Action<string> stderr)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        process.Start();
        using var cancel = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        async Task Read(StreamReader reader, Action<string> receive) { while (await reader.ReadLineAsync() is { } line) receive(line); }
        await Task.WhenAll(Read(process.StandardOutput, stdout), Read(process.StandardError, stderr), process.WaitForExitAsync());
        token.ThrowIfCancellationRequested();
        return process.ExitCode;
    }
}
