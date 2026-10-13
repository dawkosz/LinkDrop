using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LinkDrop;

public sealed class MainViewModel : Observable
{
    public Store Store { get; }
    public Downloader Downloader { get; } = new();
    public Settings Settings { get; }
    public ObservableCollection<Job> Jobs { get; }
    public ObservableCollection<Job> Pending { get; } = [];
    public ObservableCollection<Job> HomePending { get; } = [];
    public ObservableCollection<Job> History { get; } = [];
    public ObservableCollection<JobGroup> QueueGroups { get; } = [];
    public ObservableCollection<JobGroup> HistoryGroups { get; } = [];
    public ObservableCollection<JobGroup> HomeGroups { get; } = [];
    private readonly Dictionary<string, JobGroup> groupCache = [];
    public ObservableCollection<string> Formats { get; } = [];
    public ObservableCollection<string> Qualities { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public ObservableCollection<PlaylistEntry> Playlist { get; } = [];
    private bool isPlaylist;
    public bool IsPlaylist { get => isPlaylist; private set { Set(ref isPlaylist, value); Raise(nameof(AddLabel)); Raise(nameof(DownloadLabel)); } }
    public bool PlaylistSubfolder { get; set; } = true;
    public string SelectionSummary => $"Zaznaczono {Playlist.Count(p => p.Selected)} z {Playlist.Count} pozycji";
    public string AddLabel => IsPlaylist ? "＋  Zaznaczone do kolejki" : "＋  Do kolejki";
    public string DownloadLabel => IsPlaylist ? "↓  Pobierz zaznaczone" : "↓  Pobierz teraz";
    private string playlistWarning = "";
    public string PlaylistWarning { get => playlistWarning; private set => Set(ref playlistWarning, value); }
    public bool CanSubmit => !Inspecting && !IsUpdating;
    private readonly Dispatcher ui = Application.Current.Dispatcher;
    private readonly object logLock = new();
    private CancellationTokenSource? activeCancellation;
    private CancellationTokenSource? inspectCancellation;
    private CancellationTokenSource? updateCancellation;
    private Task worker = Task.CompletedTask;
    private bool closing;
    private int[] heights = [];
    public bool IsWorking => !worker.IsCompleted;
    public bool IsUpdating { get; private set; }
    private bool inspected;
    public bool Inspected { get => inspected; set => Set(ref inspected, value); }
    private bool inspecting;
    public bool Inspecting { get => inspecting; set { Set(ref inspecting, value); Raise(nameof(CanInspect)); Raise(nameof(CanSubmit)); Raise(nameof(InspectLabel)); } }
    public bool CanInspect => !Inspecting && !IsUpdating;
    public string InspectLabel => Inspecting ? "Sprawdzanie…" : "Sprawdź link";
    public bool ToolsReady => Downloader.IsReady;
    public string ToolsStatus => ToolsReady ? "Silnik gotowy" : "Brakuje narzędzi";
    private string url = "";
    public string Url { get => url; set { if (Set(ref url, value)) { inspectCancellation?.Cancel(); Inspected = false; ClearPlaylist(); heights = []; Title = "Twój kolejny dobry zapis."; Meta = "Wklej link, a sprawdzę dostępny materiał."; Thumbnail = ""; RefreshQualities(); } } }
    private string title = "Twój kolejny dobry zapis.";
    public string Title { get => title; set => Set(ref title, value); }
    private string meta = "Wklej link, a sprawdzę dostępny materiał.";
    public string Meta { get => meta; set => Set(ref meta, value); }
    private string thumbnail = "";
    public string Thumbnail { get => thumbnail; set => Set(ref thumbnail, value); }
    public string Mode { get => Settings.Mode; set { Settings.Mode = value; Raise(); RefreshFormats(); Raise(nameof(FormatHint)); } }
    public string Format { get => Settings.Format; set { if (string.IsNullOrWhiteSpace(value) || value == Settings.Format) return; Settings.Format = value; Raise(); RefreshQualities(); Raise(nameof(FormatHint)); } }
    public string Quality { get => Settings.Quality; set { if (string.IsNullOrWhiteSpace(value)) return; Settings.Quality = value; Raise(); } }
    public string Folder { get => Settings.Folder; set { Settings.Folder = value; Raise(); } }
    public bool Debug { get => Settings.Debug; set { Settings.Debug = value; Raise(); Persist(); } }
    public string FormatHint => Mode == "Wideo" ? "Jakość jest limitem rozdzielczości. MP4/MKV to kontener; kodek zależy od źródła." : Format == "Oryginał" ? "Bez ponownego kodowania. Rozszerzenie zależy od źródła." : "Konwersja nie poprawia jakości źródła. Wyższy bitrate oznacza większy plik.";
    private string notice = "Wszystko gotowe. Wklej pierwszy link.";
    public string Notice { get => notice; set => Set(ref notice, value); }
    private bool paused = true;
    public bool Paused { get => paused; set { Set(ref paused, value); Raise(nameof(QueueButton)); Raise(nameof(QueueHint)); } }
    public string QueueButton => Paused ? "▶  Uruchom kolejkę" : "Ⅱ  Wstrzymaj kolejkę";
    public string QueueHint => Paused ? "Kolejka wstrzymana · uruchom, gdy będziesz gotowy" : "Jedno pobieranie naraz · kolejne wystartuje automatycznie";
    public int CompletedCount => Jobs.Count(j => j.IsDone);
    public int PendingCount => Pending.Count;
    public bool EmptyPending => Pending.Count == 0;
    public bool EmptyHistory => History.Count == 0;
    public string LogText => string.Join(Environment.NewLine, Logs);
    public string CurrentSummary => Jobs.FirstOrDefault(j => j.State is "Pobieranie" or "Przetwarzanie")?.Title ?? "Brak aktywnego pobierania";
    public MainViewModel(string? directory = null)
    {
        Store = new(directory); var data = Store.Load(); Settings = data.Settings; Jobs = new(data.Jobs);
        RefreshFormats(); Refresh();
        Log("INFO", "LinkDrop uruchomiony. " + ToolsStatus + ".");
        if (!string.IsNullOrEmpty(Store.Warning)) Notice = Store.Warning;
        if (!ToolsReady) Notice = "Brakuje narzędzi. Uruchom scripts/Setup.ps1 z folderu projektu.";
    }
    private void RefreshFormats()
    {
        var previous = Settings.Format;
        Formats.Clear(); foreach (var f in Mode == "Audio" ? new[] { "MP3", "M4A", "Opus", "Oryginał" } : new[] { "MP4", "MKV" }) Formats.Add(f);
        Format = Formats.Contains(previous) ? previous : Formats[0];
        RefreshQualities();
        ui.BeginInvoke(() => Raise(nameof(Format)), DispatcherPriority.DataBind);
    }
    private void RefreshQualities()
    {
        var previous = Settings.Quality; Qualities.Clear();
        string[] options = Mode == "Wideo" ? new[] { "Najlepsza dostępna" }.Concat((heights.Length > 0 ? heights : [2160, 1440, 1080, 720, 480]).Select(h => h + "p")).ToArray() : Format is "MP3" or "Opus" ? ["128 kb/s", "192 kb/s", "256 kb/s", "320 kb/s"] : ["Najlepsza dostępna"];
        foreach (var option in options) Qualities.Add(option);
        Quality = Qualities.Contains(previous) ? previous : Mode == "Wideo" && Qualities.Contains("1080p") ? "1080p" : Qualities.Contains("192 kb/s") ? "192 kb/s" : Qualities[0];
        ui.BeginInvoke(() => Raise(nameof(Quality)), DispatcherPriority.DataBind);
    }
    public void Refresh()
    {
        Pending.Clear(); HomePending.Clear(); History.Clear();
        foreach (var j in Jobs.Where(j => j.CanCancel)) Pending.Add(j);
        foreach (var j in Pending.Take(3)) HomePending.Add(j);
        foreach (var j in Jobs.Where(j => !j.CanCancel).Reverse()) History.Add(j);
        RefreshGroups();
        foreach (var name in new[] { nameof(CompletedCount), nameof(PendingCount), nameof(EmptyPending), nameof(EmptyHistory), nameof(CurrentSummary) }) Raise(name);
    }
    private void RefreshGroups()
    {
        var batches = Jobs.GroupBy(JobGroup.KeyFor).ToDictionary(g => g.Key, g => g.ToArray());
        var used = new HashSet<string>();
        var queue = new List<JobGroup>(); var history = new List<JobGroup>();
        foreach (bool historyView in new[] { false, true })
        {
            var keys = (historyView ? History : Pending).Select(JobGroup.KeyFor).Distinct();
            foreach (string key in keys)
            {
                string cacheKey = (historyView ? "history:" : "queue:") + key;
                used.Add(cacheKey);
                if (!groupCache.TryGetValue(cacheKey, out var group)) { group = new JobGroup(key, historyView); groupCache.Add(cacheKey, group); }
                group.Update(batches[key]);
                (historyView ? history : queue).Add(group);
            }
        }
        foreach (var key in groupCache.Keys.Where(k => !used.Contains(k)).ToArray()) { groupCache[key].Dispose(); groupCache.Remove(key); }
        SyncGroups(QueueGroups, queue); SyncGroups(HistoryGroups, history); SyncGroups(HomeGroups, queue.Take(3).ToList());
    }
    private static void SyncGroups(ObservableCollection<JobGroup> target, IReadOnlyList<JobGroup> source)
    {
        for (int i = target.Count - 1; i >= 0; i--) if (!source.Contains(target[i])) target.RemoveAt(i);
        for (int i = 0; i < source.Count; i++)
        {
            int existing = target.IndexOf(source[i]);
            if (existing < 0) target.Insert(i, source[i]); else if (existing != i) target.Move(existing, i);
        }
    }
    public void Persist()
    {
        try { Store.Save(Settings, Jobs); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notice = "Nie można zapisać historii: " + ex.Message; }
    }
    public void Log(string level, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss}  {level,-5}  {Downloader.SafeLog(message)}";
        lock (logLock)
        {
            try
            {
                var path = Path.Combine(Store.DirectoryPath, "linkdrop.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path + ".1", true);
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        ui.BeginInvoke(() => { Logs.Add(line); while (Logs.Count > 700) Logs.RemoveAt(0); Raise(nameof(LogText)); });
    }
    public async Task Inspect()
    {
        if (!CanInspect) return;
        if (!Downloader.IsValidUrl(Url.Trim())) { Notice = "Wklej poprawny link zaczynający się od https:// lub http://."; return; }
        if (!ToolsReady) { Notice = "Brakuje narzędzi pobierania. Sprawdź Ustawienia."; return; }
        inspectCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3)); var currentUrl = Url.Trim();
        Inspected = false; ClearPlaylist();
        Inspecting = true; Notice = "Sprawdzam materiał lub wczytuję pozycje playlisty…";
        try
        {
            var result = await Downloader.Inspect(currentUrl, inspectCancellation.Token, s => Log("DEBUG", s));
            if (Url.Trim() != currentUrl) return;
            ApplyInspection(result);
        }
        catch (OperationCanceledException) { Notice = "Sprawdzanie przerwane lub przekroczono limit czasu. Możesz spróbować ponownie."; }
        catch (Exception ex) { Notice = ex.Message; Log("ERROR", ex.Message); }
        finally { Inspecting = false; inspectCancellation.Dispose(); inspectCancellation = null; }
    }
    private void ClearPlaylist()
    {
        foreach (var item in Playlist) item.PropertyChanged -= PlaylistSelectionChanged;
        Playlist.Clear(); IsPlaylist = false; PlaylistWarning = ""; Raise(nameof(SelectionSummary));
    }
    private void PlaylistSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Raise(nameof(SelectionSummary));
    public void ApplyInspection(MediaInfo result)
    {
        ClearPlaylist(); Title = result.Title;
        Meta = string.Join("  ·  ", new[] { result.Author, result.Duration, IsValidHost() }.Where(s => !string.IsNullOrEmpty(s)));
        Thumbnail = result.Thumbnail ?? ""; heights = result.Heights; RefreshQualities(); Inspected = true;
        IsPlaylist = result.Entries != null;
        foreach (var item in result.Entries ?? []) { item.PropertyChanged += PlaylistSelectionChanged; Playlist.Add(item); }
        Raise(nameof(SelectionSummary));
        PlaylistWarning = result.Truncated ? "Wyświetlono pierwsze 500 pozycji. Dalsza część tej playlisty nie została wczytana." : Playlist.Any(p => !p.Available) ? "Niedostępne pozycje są wyszarzone. Dostępność pozostałych zostanie sprawdzona podczas pobierania." : "Każda zaznaczona pozycja trafi do osobnego zadania w kolejce.";
        Notice = IsPlaylist ? Playlist.Count == 0 ? "Playlista jest pusta lub niedostępna." : "Playlista wczytana. Zaznacz pozycje, wybierz format i pobierz." : "Materiał sprawdzony. Wybierz format i dodaj do kolejki.";
    }
    private string IsValidHost() => Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host : "";
    public void SelectPlaylist(bool selected) { foreach (var item in Playlist) item.Selected = selected; }
    public int AddPlaylist(bool start)
    {
        if (!IsPlaylist || !Inspected || !CanSubmit) return 0;
        if (!ToolsReady) { Notice = "Brakuje narzędzi pobierania."; return 0; }
        if (string.IsNullOrWhiteSpace(Folder) || !Path.IsPathFullyQualified(Folder)) { Notice = "Wybierz pełną ścieżkę folderu zapisu."; return 0; }
        var selected = Playlist.Where(p => p.Selected && p.Available).ToList();
        if (selected.Count == 0) { Notice = "Zaznacz przynajmniej jedną dostępną pozycję."; return 0; }
        string folder = PlaylistSubfolder ? Path.Combine(Folder, Downloader.PlaylistFolder(Title)) : Folder;
        var existing = Jobs.Where(j => j.CanCancel || j.IsDone).Select(j => (Downloader.MediaKey(j.Url), j.Format, j.Quality)).ToHashSet();
        int added = 0; string batchId = Guid.NewGuid().ToString("N");
        foreach (var item in selected)
        {
            if (!existing.Add((Downloader.MediaKey(item.Url), Format, Quality))) continue;
            Jobs.Add(new Job { Url = item.Url, Title = item.Title, Mode = Mode, Format = Format, Quality = Quality, Folder = folder, TrackNumber = item.Number, PlaylistTitle = Title, PlaylistBatchId = batchId }); added++;
        }
        Persist(); Refresh(); Notice = $"Dodano {added} pozycji. Pominięto {selected.Count - added} duplikatów.";
        Log("INFO", Title + ": " + Notice);
        if (added > 0 && start) { Paused = false; StartWorker(); } else if (added > 0 && !Paused) StartWorker();
        return added;
    }
    public Job? Add(bool start, bool allowDuplicate = false)
    {
        if (IsPlaylist) { AddPlaylist(start); return null; }
        if (Inspecting) { Notice = "Poczekaj na zakończenie sprawdzania linku."; return null; }
        if (!Inspected && Uri.TryCreate(Url.Trim(), UriKind.Absolute, out var playlistUri) && (playlistUri.Query.Contains("list=") || playlistUri.AbsolutePath.Contains("playlist", StringComparison.OrdinalIgnoreCase) || playlistUri.AbsolutePath.Contains("album", StringComparison.OrdinalIgnoreCase))) { Notice = "Najpierw kliknij Sprawdź link, aby wczytać pozycje playlisty lub albumu."; return null; }
        if (IsUpdating) { Notice = "Poczekaj na zakończenie aktualizacji."; return null; }
        if (!ToolsReady) { Notice = "Brakuje narzędzi pobierania. Sprawdź Ustawienia."; return null; }
        string link = Url.Trim();
        if (!Downloader.IsValidUrl(link)) { Notice = "Wklej poprawny link HTTP lub HTTPS."; return null; }
        if (string.IsNullOrWhiteSpace(Folder) || !Path.IsPathFullyQualified(Folder)) { Notice = "Wybierz pełną ścieżkę folderu zapisu."; return null; }
        if (!allowDuplicate && Jobs.Any(j => Downloader.MediaKey(j.Url) == Downloader.MediaKey(link) && j.Format == Format && j.Quality == Quality && (j.CanCancel || j.IsDone))) { Notice = "Ten materiał z tymi ustawieniami jest już w kolejce lub historii."; return null; }
        var job = new Job { Url = link, Title = Inspected ? Title : new Uri(link).Host + " — " + Uri.UnescapeDataString(new Uri(link).AbsolutePath.Split('/').LastOrDefault() ?? "materiał"), Mode = Mode, Format = Format, Quality = Quality, Folder = Folder };
        Jobs.Add(job); Persist(); Refresh(); Log("INFO", "Dodano: " + job.Title); Notice = "Dodano do kolejki: " + job.Title;
        if (start) { Paused = false; StartWorker(); }
        else if (!Paused) StartWorker();
        return job;
    }
    public void ToggleQueue() { Paused = !Paused; if (!Paused) StartWorker(); }
    private void StartWorker() { if (worker.IsCompleted && !closing && !IsUpdating) worker = Work(); }
    private async Task Work()
    {
        // Yield ensures the worker task is assigned before callbacks can start another worker.
        await Task.Yield();
        while (!Paused && !closing && Jobs.FirstOrDefault(j => j.State == "Oczekuje") is { } job)
        {
            activeCancellation = new(); job.State = "Pobieranie"; job.Detail = "Łączenie ze źródłem…"; Refresh(); Persist();
            try
            {
                job.FilePath = await Downloader.Download(job, Debug, activeCancellation.Token,
                    p => ui.BeginInvoke(() => { job.Progress = p.Percent; job.Detail = p.Detail; }),
                    s => Log(Debug ? "DEBUG" : "INFO", s),
                    () => ui.BeginInvoke(() => { job.State = "Przetwarzanie"; job.Detail = "Łączenie lub konwersja pliku…"; }));
                job.State = "Gotowe"; job.Progress = 100; job.Detail = job.FilePath; Notice = "Zapisano: " + Path.GetFileName(job.FilePath); Log("OK", Notice);
            }
            catch (OperationCanceledException) { job.State = closing ? "Przerwano" : "Anulowano"; job.Detail = "Możesz ponowić pobieranie z historii."; Log("INFO", job.State + ": " + job.Title); }
            catch (Exception ex) { job.State = "Błąd"; job.Detail = ex.Message; Notice = ex.Message; Log("ERROR", ex.Message); }
            finally { activeCancellation.Dispose(); activeCancellation = null; Refresh(); Persist(); }
        }
        Raise(nameof(CurrentSummary));
    }
    public void Cancel(Job job)
    {
        if (job.State == "Oczekuje") { job.State = "Anulowano"; job.Detail = "Usunięto z kolejki."; Refresh(); Persist(); }
        else if (job.State is "Pobieranie" or "Przetwarzanie") activeCancellation?.Cancel();
    }
    public void Retry(Job job)
    {
        if (!job.CanRetry || IsUpdating) return;
        job.State = "Oczekuje"; job.Progress = 0; job.Detail = "Gotowe do ponowienia"; Refresh(); Persist();
        if (!Paused) StartWorker(); Notice = "Dodano ponownie. " + (Paused ? "Uruchom kolejkę, aby pobrać." : "");
    }
    public void CancelGroup(JobGroup group)
    {
        var jobs = group.Members.Where(j => j.CanCancel).ToArray();
        foreach (var job in jobs)
        {
            if (job.State == "Oczekuje") { job.State = "Anulowano"; job.Detail = "Anulowano pozostałe pozycje albumu."; }
            else activeCancellation?.Cancel();
        }
        Refresh(); Persist(); Notice = $"Anulowano pozostałe pozycje: {group.Title}.";
    }
    public void RetryGroup(JobGroup group)
    {
        if (IsUpdating) return;
        int count = 0;
        foreach (var job in group.Members.Where(j => j.CanRetry).ToArray()) { job.State = "Oczekuje"; job.Progress = 0; job.Detail = "Gotowe do ponowienia"; count++; }
        Refresh(); Persist();
        if (!Paused) StartWorker();
        Notice = $"Dodano ponownie {count} pozycji albumu. " + (Paused ? "Uruchom kolejkę, aby pobrać." : "");
    }
    public void MoveUp(Job job)
    {
        int index = Jobs.IndexOf(job);
        int previous = Jobs.Take(index).ToList().FindLastIndex(j => j.State == "Oczekuje" && (string.IsNullOrEmpty(job.PlaylistBatchId) || j.PlaylistBatchId == job.PlaylistBatchId));
        if (job.CanMove && previous >= 0) { Jobs.Move(index, previous); Refresh(); Persist(); }
    }
    public async Task Update()
    {
        if (IsWorking || Inspecting || IsUpdating) { Notice = "Zakończ pobieranie i sprawdzanie linku przed aktualizacją."; return; }
        Paused = true; IsUpdating = true; Raise(nameof(CanInspect)); Raise(nameof(CanSubmit)); updateCancellation = new(TimeSpan.FromMinutes(3)); Notice = "Aktualizuję yt-dlp…";
        try { await Downloader.Update(updateCancellation.Token, s => Log("UPDATE", s)); Notice = "Sprawdzono aktualizację yt-dlp. Szczegóły w logach."; }
        catch (Exception ex) { Notice = "Aktualizacja przerwana: " + ex.Message; Log("ERROR", Notice); }
        finally { IsUpdating = false; Raise(nameof(CanInspect)); Raise(nameof(CanSubmit)); updateCancellation.Dispose(); updateCancellation = null; }
    }
    public async Task Stop()
    {
        closing = true; Paused = true; inspectCancellation?.Cancel(); activeCancellation?.Cancel(); updateCancellation?.Cancel();
        await worker; Persist();
        foreach (var group in groupCache.Values) group.Dispose();
    }
}
