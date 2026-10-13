using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LinkDrop;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Raise(name); return true; }
    public void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class Job : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Mode { get; set; } = "Audio";
    public string Format { get; set; } = "MP3";
    public string Quality { get; set; } = "192 kb/s";
    public string Folder { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int TrackNumber { get; set; }
    public string PlaylistTitle { get; set; } = "";
    public string PlaylistBatchId { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    private string state = "Oczekuje";
    public string State { get => state; set { if (Set(ref state, value)) { Raise(nameof(CanCancel)); Raise(nameof(CanRetry)); Raise(nameof(IsDone)); Raise(nameof(CanMove)); } } }
    private double progress;
    public double Progress { get => progress; set => Set(ref progress, value); }
    private string detail = "Gotowe do pobrania";
    public string Detail { get => detail; set => Set(ref detail, value); }
    [JsonIgnore] public bool CanCancel => State is "Oczekuje" or "Pobieranie" or "Przetwarzanie";
    [JsonIgnore] public bool CanRetry => State is "Błąd" or "Anulowano" or "Przerwano";
    [JsonIgnore] public bool CanMove => State == "Oczekuje";
    [JsonIgnore] public bool IsDone => State == "Gotowe";
    [JsonIgnore] public string Description => $"{Format}  ·  {Quality}  ·  {Created:dd.MM, HH:mm}";
    [JsonIgnore] public string Symbol => Mode == "Audio" ? "♫" : "▶";
}

public class Settings
{
    public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "LinkDrop");
    public string Format { get; set; } = "MP3";
    public string Quality { get; set; } = "192 kb/s";
    public string Mode { get; set; } = "Audio";
    public bool Debug { get; set; }
}
public class SavedData
{
    public Settings Settings { get; set; } = new();
    public List<Job> Jobs { get; set; } = [];
}
public class Store
{
    public string DirectoryPath { get; }
    public string Warning { get; private set; } = "";
    private string FilePath => Path.Combine(DirectoryPath, "state.json");
    public Store(string? directory = null)
    { DirectoryPath = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkDrop"); Directory.CreateDirectory(DirectoryPath); }
    public SavedData Load()
    {
        foreach (var path in new[] { FilePath, FilePath + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var data = JsonSerializer.Deserialize<SavedData>(File.ReadAllText(path)) ?? throw new InvalidDataException("Pusty zapis");
                if (data.Settings is null || data.Jobs is null) throw new InvalidDataException("Niepełny zapis");
                foreach (var job in data.Jobs.Where(j => j.State is "Pobieranie" or "Przetwarzanie")) { job.State = "Przerwano"; job.Detail = "Przerwane zamknięciem aplikacji — możesz ponowić."; }
                MigrateAlbumGroups(data.Jobs);
                if (path.EndsWith(".bak")) Warning = "Odtworzono dane z kopii zapasowej.";
                return data;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { Warning = "Nie udało się odczytać zapisu. Zachowano go do diagnostyki."; }
        }
        return new();
    }
    public void Save(Settings settings, IEnumerable<Job> jobs)
    {
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new SavedData { Settings = settings, Jobs = jobs.ToList() }, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak"); else File.Move(temp, FilePath);
    }
    public static void MigrateAlbumGroups(IEnumerable<Job> jobs)
    {
        // Version 1.1 did not store batch IDs. Its batch additions are contiguous
        // and have nearly identical creation times. Do not group by title alone.
        Job? previous = null; string batch = "";
        foreach (var job in jobs)
        {
            if (string.IsNullOrWhiteSpace(job.PlaylistTitle) || !string.IsNullOrEmpty(job.PlaylistBatchId)) { previous = null; continue; }
            bool same = previous != null && previous.PlaylistTitle == job.PlaylistTitle && previous.Folder == job.Folder && previous.Format == job.Format && previous.Quality == job.Quality && Math.Abs((job.Created - previous.Created).TotalSeconds) < 2 && (job.TrackNumber == 0 || previous.TrackNumber == 0 || job.TrackNumber > previous.TrackNumber);
            if (!same) batch = "legacy-" + job.Id;
            job.PlaylistBatchId = batch; previous = job;
        }
    }
}

public class PlaylistEntry : Observable
{
    public int Number { get; init; }
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Available { get; init; }
    private bool selected;
    public bool Selected { get => selected; set => Set(ref selected, value && Available); }
}
public record MediaInfo(string Title, string Author, string Duration, string? Thumbnail, int[] Heights,
    IReadOnlyList<PlaylistEntry>? Entries = null, bool Truncated = false);
public record TransferProgress(double Percent, string Detail);
