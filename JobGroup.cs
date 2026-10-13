using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LinkDrop;

/// <summary>A view of one persisted batch. Queue/history share jobs, not expansion state.</summary>
public sealed class JobGroup : Observable, IDisposable
{
    private List<Job> members = [];
    private bool expanded;
    private bool activated;
    public string Key { get; }
    public bool HistoryView { get; }
    public ObservableCollection<Job> Items { get; } = [];
    public IReadOnlyList<Job> Members => members;
    public bool IsAlbum => members.Count > 0 && !string.IsNullOrEmpty(members[0].PlaylistBatchId);
    public Job? SingleJob => members.FirstOrDefault();
    public string Title => IsAlbum ? members[0].PlaylistTitle : SingleJob?.Title ?? "";
    public string Folder => SingleJob?.Folder ?? "";
    public bool IsExpanded { get => expanded; set { if (Set(ref expanded, value)) Raise(nameof(Arrow)); } }
    public string Arrow => IsExpanded ? "⌄" : "›";
    public int Total => members.Count;
    public int Completed => members.Count(j => j.IsDone);
    public int Pending => members.Count(j => j.CanCancel);
    public int Failed => members.Count(j => j.State == "Błąd");
    public int Interrupted => members.Count(j => j.State is "Anulowano" or "Przerwano");
    public bool HasPending => Pending > 0;
    public bool CanCancelGroup => !HistoryView && HasPending;
    public bool CanRetryGroup => HistoryView && members.Any(j => j.CanRetry);
    private Job? Active => members.FirstOrDefault(j => j.State is "Pobieranie" or "Przetwarzanie");
    public double Progress => Total == 0 ? 0 : members.Sum(j => j.IsDone ? 100 : j.State is "Pobieranie" or "Przetwarzanie" ? Math.Min(99, j.Progress) : 0) / Total;
    public string Summary
    {
        get
        {
            var parts = new List<string> { $"{Completed} z {Total} gotowych" };
            if (Pending > 0) parts.Add($"{Pending} w kolejce");
            if (Failed > 0) parts.Add($"błędy: {Failed}");
            if (Interrupted > 0) parts.Add($"przerwane: {Interrupted}");
            return string.Join("  ·  ", parts);
        }
    }
    public string Metadata => SingleJob == null ? "" : $"{SingleJob.Format}  ·  {SingleJob.Quality}  ·  {members.Min(j => j.Created):dd.MM, HH:mm}";
    public string Status => Active?.State ?? (HasPending ? "Oczekuje" : Failed > 0 ? "Z błędami" : Interrupted > 0 ? "Przerwano" : "Gotowe");
    public string Activity => Active is { } current ? $"{current.State}: {current.Title}  ·  {current.Detail}" : HistoryView ? $"Rozwiń, aby zobaczyć zakończone pozycje ({Items.Count})." : $"Rozwiń, aby zobaczyć kolejkę utworów ({Items.Count}).";
    public JobGroup(string key, bool history) { Key = key; HistoryView = history; }
    public static string KeyFor(Job job) => string.IsNullOrEmpty(job.PlaylistBatchId) ? "single:" + job.Id : "album:" + job.PlaylistBatchId;
    public void Update(IEnumerable<Job> jobs)
    {
        var next = jobs.ToList();
        if (!members.SequenceEqual(next))
        {
            foreach (var item in members) item.PropertyChanged -= JobChanged;
            members = next;
            foreach (var item in members) item.PropertyChanged += JobChanged;
        }
        RefreshItems(); Notify();
    }
    private void JobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Job.State)) RefreshItems();
        if (e.PropertyName is nameof(Job.State) or nameof(Job.Progress) or nameof(Job.Detail)) Notify();
    }
    private void RefreshItems()
    {
        var visible = members.Where(j => HistoryView ? !j.CanCancel : j.CanCancel).ToArray();
        if (Items.SequenceEqual(visible)) return;
        Items.Clear(); foreach (var job in visible) Items.Add(job);
    }
    private void Notify()
    {
        if (!HistoryView && Active != null && !activated) { activated = true; IsExpanded = true; }
        foreach (string name in new[] { nameof(IsAlbum), nameof(SingleJob), nameof(Title), nameof(Folder), nameof(Total), nameof(Completed), nameof(Pending), nameof(Failed), nameof(Interrupted), nameof(HasPending), nameof(CanCancelGroup), nameof(CanRetryGroup), nameof(Progress), nameof(Summary), nameof(Metadata), nameof(Status), nameof(Activity) }) Raise(name);
    }
    public void Dispose() { foreach (var job in members) job.PropertyChanged -= JobChanged; }
}
