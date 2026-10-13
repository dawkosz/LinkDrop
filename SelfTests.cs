using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LinkDrop;

public static class SelfTests
{
    public static async Task<int> RunPlaylistNetwork(string url, string directory)
    {
        Directory.CreateDirectory(directory); var report = new List<string>();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var downloader = new Downloader();
            var info = await downloader.Inspect(url, timeout.Token, s => report.Add(s));
            if (info.Entries == null || info.Entries.Count < 2) throw new Exception("Expected a playlist with at least two entries");
            report.Add($"PASS: real watch URL with list parameter returns playlist: {info.Title} ({info.Entries.Count} entries)");
            var model = new MainViewModel(Path.Combine(directory, "profile")) { Url = url, Folder = Path.Combine(directory, "downloads") };
            model.ApplyInspection(info);
            int added = model.AddPlaylist(false);
            if (added < 2 || !model.Paused || model.Pending.Any(j => j.Url == url)) throw new Exception("Individual queue expansion failed");
            report.Add($"PASS: {added} individual items queued, no downloads started");
            foreach (var entry in info.Entries) report.Add($"{entry.Number:D2} | {entry.Title} | {entry.Available} | {entry.Url}");
            File.WriteAllText(Path.Combine(directory, "playlist-info.json"), JsonSerializer.Serialize(info));
            await model.Stop();
            File.WriteAllLines(Path.Combine(directory, "playlist-results.txt"), report); return 0;
        }
        catch (Exception ex) { report.Add("FAIL: " + ex); File.WriteAllLines(Path.Combine(directory, "playlist-results.txt"), report); return 1; }
    }
    public static async Task<int> RunNetwork(string directory)
    {
        Directory.CreateDirectory(directory); var report = new List<string>();
        try
        {
            var downloader = new Downloader(); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            const string url = "https://www.youtube.com/watch?v=jNQXAC9IVRw";
            var info = await downloader.Inspect(url, timeout.Token, s => report.Add(s));
            report.Add("PASS: YouTube metadata: " + info.Title + " / " + info.Duration);
            var job = new Job { Url = url, Title = info.Title, Folder = directory, Mode = "Audio", Format = "MP3", Quality = "192 kb/s" };
            var output = await downloader.Download(job, false, timeout.Token, _ => { }, s => report.Add(s), () => { });
            report.Add("PASS: Real YouTube download to MP3: " + Path.GetFileName(output));
            File.WriteAllLines(Path.Combine(directory, "network-results.txt"), report); return 0;
        }
        catch (Exception ex) { report.Add("NETWORK TEST FAILED: " + ex.Message); File.WriteAllLines(Path.Combine(directory, "network-results.txt"), report); return 1; }
    }
    public static async Task<int> Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var report = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); report.Add("PASS: " + name); File.WriteAllLines(Path.Combine(directory, "results.txt"), report); }
        try
        {
            var downloader = new Downloader();
            Check(downloader.IsReady, "All bundled tools present");
            Check(Downloader.IsValidUrl("https://example.com/a?v=1&list=2") && !Downloader.IsValidUrl("file:///C:/test") && !Downloader.IsValidUrl("--exec calc") && !Downloader.IsValidUrl("https://user:pass@example.com"), "URL validation rejects file paths, flags and credentials");
            var p = Downloader.ParseProgress("LD_PROGRESS: 42.5%| 2.3MiB/s| 00:12");
            Check(p?.Percent == 42.5 && Downloader.ParseProgress("LD_PROGRESS:NA|NA|NA") is null, "Real progress parsing and unknown totals");
            Check(!Downloader.SafeLog("ERROR https://example.com/video?token=secret&key=123").Contains("secret"), "Query secrets removed from logs");
            var job = new Job { Url = "https://example.com/clip?x=1&y=2", Folder = Path.Combine(directory, "pliki ze spacją ąę"), Format = "MP3", Quality = "192 kb/s" };
            var args = downloader.BuildArguments(job);
            Check(args[^1] == job.Url && args[^2] == "--" && args.Contains(job.Folder), "Arguments preserve URL and Unicode paths as single values");
            var store = new Store(Path.Combine(directory, "storage"));
            job.State = "Pobieranie"; store.Save(new Settings(), [job]);
            var restored = store.Load(); Check(restored.Jobs[0].State == "Przerwano", "Interrupted download restored safely");
            store.Save(new Settings(), [job]); File.WriteAllText(Path.Combine(store.DirectoryPath, "state.json"), "invalid json");
            Check(store.Load().Jobs.Count == 1 && store.Warning.Contains("kopii"), "Corrupt state recovered from atomic backup");
            await GroupTests.Run(directory, Check);
            string fixture = Path.Combine(directory, "fixture.mp4");
            var stderr = new StringBuilder();
            int generated = await Downloader.Run(Path.Combine(downloader.ToolsDirectory, "ffmpeg.exe"), ["-y", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "3", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", fixture], CancellationToken.None, _ => { }, s => stderr.AppendLine(s));
            Check(generated == 0 && File.Exists(fixture), "FFmpeg generates owned 3-second video/audio fixture");
            await using var server = new FixtureServer(await File.ReadAllBytesAsync(fixture));
            string url = server.Url + "fixture.mp4";
            var info = await downloader.Inspect(url, CancellationToken.None, s => report.Add("inspect: " + s));
            Check(!string.IsNullOrEmpty(info.Title), "yt-dlp extracts metadata over local HTTP");
            var inspectArgs = downloader.InspectionArguments("https://www.youtube.com/watch?v=abcdefghijk&list=PLexampleplaylist");
            Check(inspectArgs.Contains("--yes-playlist") && inspectArgs.Contains("--flat-playlist") && !inspectArgs.Contains("--no-playlist"), "Watch URLs with list use playlist discovery");
            var playlistJson = JsonSerializer.Serialize(new { title = "Album / test: 2026", entries = new object?[] {
                new { title = "Pierwszy", url = url + "?track=1", playlist_index = 1 },
                new { title = "Drugi", url = url + "?track=2", playlist_index = 2 },
                new { title = "Trzeci", url = url + "?track=3", playlist_index = 3 },
                new { title = "[Private video]", url = "https://www.youtube.com/watch?v=abcdefghijk", availability = "private" }, null } });
            var playlistInfo = Downloader.ParseInspection(playlistJson);
            Check(playlistInfo.Entries?.Count == 5 && playlistInfo.Entries.Count(x => x.Available) == 3, "Playlist parser keeps unavailable entries and original order");
            Check(Downloader.PlaylistFolder("CON") == "_CON" && !Downloader.PlaylistFolder("../a:b%?").Contains('/') && !Downloader.PlaylistFolder("../a:b%?").Contains('%'), "Playlist folder handles unsafe and Windows reserved names");
            Check(Downloader.MediaKey("https://music.youtube.com/watch?v=abcdefghijk&list=xyz") == Downloader.MediaKey("https://youtu.be/abcdefghijk"), "YouTube duplicate detection normalizes video links");
            var playlistModel = new MainViewModel(Path.Combine(directory, "playlist-profile")) { Url = "https://example.com/album/test", Folder = Path.Combine(directory, "playlist-output") };
            playlistModel.ApplyInspection(playlistInfo); playlistModel.SelectPlaylist(false);
            Check(playlistModel.AddPlaylist(false) == 0 && playlistModel.Jobs.Count == 0, "Empty playlist selection does not queue or download");
            playlistModel.SelectPlaylist(true); playlistModel.Playlist[1].Selected = false;
            Check(playlistModel.AddPlaylist(false) == 2 && playlistModel.Paused && playlistModel.Pending.Select(j => j.TrackNumber).SequenceEqual(new[] { 1, 3 }), "Only selected available entries queued with original numbering");
            Check(playlistModel.AddPlaylist(false) == 0 && playlistModel.Jobs.Count == 2, "Re-adding playlist skips duplicates");
            Check(playlistModel.Jobs.All(j => j.Folder == Path.Combine(playlistModel.Folder, "Album _ test_ 2026") && downloader.BuildArguments(j).Contains("--no-playlist")), "Album subfolder and individual download isolation");
            var playlistRestored = new Store(playlistModel.Store.DirectoryPath).Load();
            Check(playlistRestored.Jobs.Select(j => j.TrackNumber).SequenceEqual(new[] { 1, 3 }), "Playlist order and paths persist across restart");
            playlistModel.ToggleQueue();
            var playlistDeadline = DateTime.UtcNow.AddSeconds(90);
            while (playlistModel.IsWorking && DateTime.UtcNow < playlistDeadline) await Task.Delay(100);
            Check(playlistModel.CompletedCount == 2 && playlistModel.Jobs.All(j => j.TrackNumber > 0 && Path.GetFileName(j.FilePath).StartsWith("fixture [fixture]")), "Playlist tracks retain internal order but downloaded filenames start with source title, without number prefix");
            Check(playlistModel.QueueGroups.Count == 0 && playlistModel.HistoryGroups.Count == 1 && playlistModel.HistoryGroups[0].Completed == 2 && playlistModel.HistoryGroups[0].Progress == 100, "Real playlist downloads finish as one complete history album");
            playlistModel.Url = url;
            Check(!playlistModel.IsPlaylist && playlistModel.Playlist.Count == 0 && !playlistModel.Inspected, "URL edit discards stale playlist selection");
            await playlistModel.Stop();
            foreach (var format in new[] { "MP3", "M4A", "Opus", "Oryginał", "MP4", "MKV" })
            {
                var current = new Job { Url = url, Title = "Test " + format, Folder = job.Folder, Format = format, Mode = format is "MP4" or "MKV" ? "Wideo" : "Audio", Quality = format is "MP3" or "Opus" ? "192 kb/s" : "Najlepsza dostępna" };
                string output = await downloader.Download(current, true, CancellationToken.None, _ => { }, s => report.Add(format + ": " + s), () => { });
                Check(File.Exists(output) && new FileInfo(output).Length > 1000, "Actual download/conversion: " + format);
                var probe = new StringBuilder();
                int exit = await Downloader.Run(Path.Combine(downloader.ToolsDirectory, "ffprobe.exe"), ["-v", "error", "-show_streams", "-of", "json", output], CancellationToken.None, s => probe.AppendLine(s), s => report.Add(s));
                using var data = JsonDocument.Parse(probe.ToString());
                var streams = data.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                Check(exit == 0 && streams.Any(s => s.GetProperty("codec_type").GetString() == "audio") && (current.Mode != "Audio" || streams.All(s => s.GetProperty("codec_type").GetString() != "video")), "ffprobe validates streams: " + format);
                if (format == "MP3") Check(streams[0].GetProperty("codec_name").GetString() == "mp3" && int.Parse(streams[0].GetProperty("bit_rate").GetString()!) == 192000, "MP3 codec and requested 192 kb/s verified");
            }
            using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(700)))
            {
                bool cancelled = false;
                var watch = Stopwatch.StartNew();
                try { await Downloader.Run(Path.Combine(downloader.ToolsDirectory, "ffmpeg.exe"), ["-re", "-f", "lavfi", "-i", "sine=frequency=440", "-f", "null", "-"], cancel.Token, _ => { }, _ => { }); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && watch.Elapsed < TimeSpan.FromSeconds(10), "Cancellation terminates an active real child process");
            }
            var model = new MainViewModel(Path.Combine(directory, "viewmodel"));
            model.Folder = Path.Combine(directory, "queue-output"); model.Url = "bad url";
            Check(model.Add(false) == null, "Queue rejects invalid URL");
            model.Url = url; model.Mode = "Audio"; model.Format = "MP3"; model.Quality = "192 kb/s";
            var first = model.Add(false)!;
            Check(first != null && model.Paused && model.PendingCount == 1, "Add to queue does not start paused queue");
            Check(model.Add(false) == null && model.PendingCount == 1, "Duplicate prevention");
            model.Format = "M4A"; var second = model.Add(false)!;
            model.MoveUp(second); Check(model.Pending[0] == second, "Queue reorder");
            model.Cancel(second); Check(second.State == "Anulowano" && model.History.Contains(second), "Cancel queued item and record history");
            model.Retry(second); Check(second.State == "Oczekuje", "Retry cancelled item");
            model.ToggleQueue();
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (model.IsWorking && DateTime.UtcNow < deadline) await Task.Delay(100);
            Check(!model.IsWorking && model.CompletedCount == 2 && model.PendingCount == 0, "Real sequential queue completes two conversions");
            model.Url = server.Url + "missing.mp4";
            var missing = model.Add(true)!;
            deadline = DateTime.UtcNow.AddSeconds(40);
            while (model.IsWorking && DateTime.UtcNow < deadline) await Task.Delay(100);
            Check(missing.State == "Błąd" && model.History.Contains(missing), "HTTP failure recorded without crashing worker");
            await model.Stop();
            var reloaded = new Store(model.Store.DirectoryPath).Load();
            Check(reloaded.Jobs.Count == 3 && reloaded.Jobs.Count(j => j.IsDone) == 2, "Completed history survives restart");
            report.Add("ALL TESTS PASSED"); File.WriteAllLines(Path.Combine(directory, "results.txt"), report); return 0;
        }
        catch (Exception ex) { report.Add(ex.ToString()); File.WriteAllLines(Path.Combine(directory, "results.txt"), report); return 1; }
    }
    public static async Task RunUi(MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<string>();
        try
        {
            var model = window.Model;
            model.Jobs.Clear(); model.Refresh(); model.Url = "";
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(window, Path.Combine(directory, "01-home.png"));
            var add = (Button)window.FindName("AddButton");
            model.Url = "niepoprawny link"; add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (model.PendingCount != 0 || !model.Notice.Contains("poprawny")) throw new Exception("Invalid input UI failed");
            results.Add("PASS: actual Home add button validates input");
            ((Button)window.FindName("VideoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (model.Mode != "Wideo" || !model.Formats.Contains("MP4")) throw new Exception("Video selection failed");
            ((Button)window.FindName("AudioButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (model.Mode != "Audio" || model.Format != "MP3") throw new Exception("Audio selection failed");
            results.Add("PASS: actual audio/video buttons update format and quality selectors");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (((ComboBox)window.FindName("QualitySelect")).SelectedItem?.ToString() != model.Quality) throw new Exception("Visible quality selection is blank or out of sync");
            results.Add("PASS: visible quality selection stays synchronized after mode changes");
            model.Jobs.Clear(); model.Refresh();
            model.Url = "https://example.com/demo.mp4"; model.Title = "Wieczorny spacer — nagranie terenowe"; model.Meta = "Materiał demonstracyjny  ·  3:42"; model.Inspected = true;
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (model.PendingCount != 1) throw new Exception("Add via button failed");
            results.Add("PASS: actual add button creates queue entry");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (((ComboBox)window.FindName("QualitySelect")).SelectedItem?.ToString() != model.Quality) throw new Exception("Visible quality selection lost after URL edit");
            results.Add("PASS: quality selection preserved when editing URL");
            model.Jobs.Add(new Job { Title = "Leśne dźwięki — własne nagranie", Mode = "Audio", Format = "MP3", Quality = "192 kb/s", State = "Gotowe", Progress = 100, Detail = "Przykładowy wpis do testu układu", FilePath = "C:\\demo\\nagranie.mp3" });
            model.Refresh(); model.Log("INFO", "Test interfejsu: nawigacja i układ.");
            foreach (string page in new[] { "Home", "Queue", "History", "Logs", "Settings" })
            {
                window.Navigate(page); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture(window, Path.Combine(directory, "02-" + page.ToLowerInvariant() + ".png")); results.Add("PASS: renders " + page);
            }
            window.Width = 960; window.Height = 700; window.Navigate("Home");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(window, Path.Combine(directory, "03-home-small.png"));
            results.Add("PASS: renders minimum window 960x700");
            window.Width = 1180; window.Height = 1000; model.Jobs.Clear(); model.Refresh();
            model.Url = "https://www.youtube.com/watch?v=abcdefghijk&list=PLexampleplaylist";
            var playlistFile = Path.Combine(directory, "playlist-info.json");
            var playlist = File.Exists(playlistFile) ? JsonSerializer.Deserialize<MediaInfo>(File.ReadAllText(playlistFile))! : Downloader.ParseInspection("""
                {"title":"Album testowy","entries":[{"title":"Pierwszy utwór","url":"https://example.com/one.mp4"},{"title":"Drugi utwór","url":"https://example.com/two.mp4"},{"title":"[Private video]","url":"https://example.com/private.mp4"}]}
                """);
            model.ApplyInspection(playlist); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (((FrameworkElement)window.FindName("PlaylistPanel")).Visibility != Visibility.Visible) throw new Exception("Playlist panel not visible");
            ((Button)window.FindName("SelectNoneButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (model.Jobs.Count != 0) throw new Exception("Empty selection queued entries");
            ((Button)window.FindName("SelectAllButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var available = model.Playlist.Where(p => p.Available).ToList();
            if (available.Count > 1) available[1].Selected = false;
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (model.PendingCount != available.Count - 1 || !model.Paused) throw new Exception("Playlist UI selection not respected");
            results.Add("PASS: playlist panel, select none/all and actual Add button respect selection without single-video prompt");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(window, Path.Combine(directory, "04-playlist.png"));
            ((Button)window.FindName("DownloadButton")).BringIntoView();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(window, Path.Combine(directory, "05-playlist-actions.png"));
            await RunGroupedUi(window, directory, results);
            File.WriteAllLines(Path.Combine(directory, "ui-results.txt"), results);
            await model.Stop(); window.Close();
        }
        catch (Exception ex) { results.Add("FAIL: " + ex); File.WriteAllLines(Path.Combine(directory, "ui-results.txt"), results); Application.Current.Shutdown(1); }
    }
    private static async Task RunGroupedUi(MainWindow window, string directory, List<string> results)
    {
        var model = window.Model; model.Jobs.Clear(); model.Refresh(); window.Width = 1180; window.Height = 900;
        var batch = Guid.NewGuid().ToString("N");
        var jobs = Enumerable.Range(1, 5).Select(i => new Job { Title = "Utwór " + i, PlaylistTitle = "OPA7 — album demonstracyjny", PlaylistBatchId = batch, TrackNumber = i, State = i < 3 ? "Gotowe" : i == 3 ? "Pobieranie" : "Oczekuje", Progress = i < 3 ? 100 : i == 3 ? 42 : 0, Detail = i == 3 ? "2,4 MiB/s · pozostało 00:18" : "Wpis testowy", Folder = directory }).ToArray();
        foreach (var job in jobs) model.Jobs.Add(job);
        model.Jobs.Add(new Job { Title = "Pojedyncze nagranie", State = "Oczekuje" });
        model.Refresh(); window.Navigate("Queue");
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var album = model.QueueGroups.Single(g => g.IsAlbum);
        Button Toggle(FrameworkElement root, JobGroup group) => Descendants(root).OfType<Button>().First(b => b.Tag?.ToString() == "ToggleGroup" && ReferenceEquals(b.DataContext, group));
        var toggle = Toggle((FrameworkElement)window.FindName("QueueGroupList"), album);
        if (!album.IsExpanded) throw new Exception("Active album did not open automatically");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        jobs[2].Progress = 64; model.Refresh();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (album.IsExpanded || model.QueueGroups.Count != 2 || !album.Activity.Contains("Utwór 3")) throw new Exception("Collapsed album/live summary failed");
        Capture(window, Path.Combine(directory, "06-queue-grouped.png"));
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (!album.IsExpanded || album.Items.Count != 3) throw new Exception("Queue expand failed");
        Capture(window, Path.Combine(directory, "07-queue-expanded.png"));
        results.Add("PASS: actual album header toggles queue details and remains collapsed through live progress");
        jobs[2].State = "Błąd"; jobs[2].Detail = "Przykładowy błąd źródła"; jobs[3].State = "Gotowe"; jobs[4].State = "Anulowano";
        model.Refresh(); window.Navigate("History");
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var history = model.HistoryGroups.Single(g => g.IsAlbum);
        if (history.IsExpanded || history.Completed != 3 || history.Failed != 1 || history.Items.Count != 5) throw new Exception("History album summary failed");
        Capture(window, Path.Combine(directory, "08-history-grouped.png"));
        Toggle((FrameworkElement)window.FindName("HistoryGroupList"), history).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Capture(window, Path.Combine(directory, "09-history-expanded.png"));
        var retry = Descendants((FrameworkElement)window.FindName("HistoryGroupList")).OfType<Button>().First(b => b.Tag?.ToString() == "RetryGroup" && ReferenceEquals(b.DataContext, history));
        retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (jobs[2].State != "Oczekuje" || jobs[4].State != "Oczekuje" || model.HistoryGroups.Single(g => g.IsAlbum).Completed != 3) throw new Exception("History group retry button failed");
        results.Add("PASS: history album collapses/expands and actual retry button requeues only unsuccessful tracks");
        window.Navigate("Queue"); window.Width = 960; window.Height = 700;
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Capture(window, Path.Combine(directory, "10-groups-small.png"));
        var cancel = Descendants((FrameworkElement)window.FindName("QueueGroupList")).OfType<Button>().First(b => b.Tag?.ToString() == "CancelGroup");
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (jobs[2].State != "Anulowano" || jobs[4].State != "Anulowano" || model.PendingCount != 1) throw new Exception("Group cancel button affected wrong jobs");
        results.Add("PASS: group cancel button preserves other downloads; grouped layout renders at minimum size");
        var oldData = Path.Combine(directory, "existing-history");
        if (File.Exists(Path.Combine(oldData, "state.json")))
        {
            var loaded = new Store(oldData).Load(); model.Jobs.Clear(); foreach (var job in loaded.Jobs) model.Jobs.Add(job); model.Refresh();
            window.Width = 1180; window.Height = 900; window.Navigate("History");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(window, Path.Combine(directory, "11-existing-history.png"));
            if (model.HistoryGroups.Any(g => g.IsAlbum)) model.HistoryGroups.First(g => g.IsAlbum).IsExpanded = true;
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(window, Path.Combine(directory, "12-existing-history-expanded.png"));
            results.Add("PASS: isolated copy of existing user history renders after album migration");
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class FixtureServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly byte[] bytes;
        private readonly Task loop;
        public string Url { get; }
        public FixtureServer(byte[] bytes) { this.bytes = bytes; listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/"; loop = Serve(); }
        private async Task Serve()
        {
            try { while (!stop.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stop.Token); _ = Handle(client); } }
            catch (OperationCanceledException) { }
        }
        private async Task Handle(TcpClient client)
        {
            using (client)
            try
            {
                var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                var request = await reader.ReadLineAsync() ?? "";
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                bool found = request.Contains("/fixture.mp4");
                var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Type: video/mp4\r\nContent-Length: {(found ? bytes.Length : 0)}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header);
                if (found && !request.StartsWith("HEAD")) await stream.WriteAsync(bytes);
            }
            catch (IOException) { }
        }
        public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); await loop; stop.Dispose(); }
    }
}
