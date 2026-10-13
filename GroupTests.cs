using System.IO;

namespace LinkDrop;

public static class GroupTests
{
    public static async Task Run(string directory, Action<bool, string> check)
    {
        var model = new MainViewModel(Path.Combine(directory, "groups-profile"));
        Job Track(string batch, string state, int number) => new() { PlaylistBatchId = batch, PlaylistTitle = "Ten sam tytuł albumu", TrackNumber = number, Title = "Utwór " + number, State = state, Folder = directory, Url = "https://example.com/" + batch + number };
        var done = Track("batch-a", "Gotowe", 1);
        var active = Track("batch-a", "Pobieranie", 2); active.Progress = 50; active.Detail = "2 MiB/s";
        var waiting = Track("batch-a", "Oczekuje", 3);
        var failed = Track("batch-a", "Błąd", 4);
        var other = Track("batch-b", "Oczekuje", 1);
        var cancelled = Track("batch-b", "Anulowano", 2);
        var single = new Job { Title = "Pojedynczy plik", State = "Oczekuje" };
        var singleDone = new Job { Title = "Pojedynczy zapis", State = "Gotowe" };
        foreach (var job in new[] { done, active, waiting, failed, other, cancelled, single, singleDone }) model.Jobs.Add(job);
        model.Refresh();
        check(model.QueueGroups.Count == 3 && model.HistoryGroups.Count == 3, "Queue and history group albums while retaining standalone rows");
        var queue = model.QueueGroups.Single(g => g.Key == "album:batch-a");
        var history = model.HistoryGroups.Single(g => g.Key == "album:batch-a");
        check(queue.Items.SequenceEqual(new[] { active, waiting }) && history.Items.SequenceEqual(new[] { done, failed }), "Partial album exposes correct queue/history subsets without losing whole-album totals");
        check(queue.Total == 4 && queue.Completed == 1 && queue.Failed == 1 && Math.Abs(queue.Progress - 37.5) < .001, "Album progress weights completed and active tracks correctly");
        check(queue.Activity.Contains(active.Title) && queue.Activity.Contains("2 MiB/s") && queue.IsExpanded && !history.IsExpanded, "Active album opens once and exposes live track details; history starts collapsed");
        queue.IsExpanded = false; active.Progress = 75; active.Detail = "3 MiB/s"; model.Refresh();
        check(ReferenceEquals(queue, model.QueueGroups.Single(g => g.Key == queue.Key)) && !queue.IsExpanded && queue.Activity.Contains("3 MiB/s") && Math.Abs(queue.Progress - 43.75) < .001, "Live progress and refresh preserve manual collapse and group identity");
        check(model.QueueGroups.Count(g => g.IsAlbum && g.Title == queue.Title) == 2, "Identically named albums with different batch IDs remain separate");
        active.State = "Gotowe"; active.Progress = 100; model.Refresh();
        check(queue.Completed == 2 && history.Items.Count == 3 && history.Items.Contains(active), "Completed track moves into existing history album and updates counts");
        history.IsExpanded = true;
        model.RetryGroup(history);
        check(failed.State == "Oczekuje" && done.IsDone && active.IsDone && model.Jobs.Count == 8 && failed.PlaylistBatchId == "batch-a", "Group retry affects only failed/interrupted tracks and keeps batch identity");
        check(history.IsExpanded, "Refreshing after retry preserves history expansion");
        model.CancelGroup(queue);
        check(waiting.State == "Anulowano" && failed.State == "Anulowano" && done.IsDone && active.IsDone && other.State == "Oczekuje", "Cancel remaining affects only unfinished tracks in chosen album");
        check(model.QueueGroups.All(g => g.Key != queue.Key) && model.HistoryGroups.Single(g => g.Key == queue.Key).Items.Count == 4, "Finished album disappears from queue and remains one complete history card");
        model.RetryGroup(model.HistoryGroups.Single(g => g.Key == queue.Key));
        check(model.QueueGroups.Single(g => g.Key == queue.Key).Items.Count == 2 && model.HistoryGroups.Single(g => g.Key == queue.Key).Completed == 2, "Retrying cancelled album restores only unfinished subset");
        model.Persist();
        var restored = new Store(model.Store.DirectoryPath).Load();
        check(restored.Jobs.Where(j => j.PlaylistBatchId == "batch-a").Count() == 4 && restored.Jobs.Count(j => string.IsNullOrEmpty(j.PlaylistBatchId)) == 2, "Batch identity survives persisted queue/history restart");
        await model.Stop();

        var stamp = new DateTime(2026, 9, 20, 12, 0, 0);
        Job Legacy(int number, int minutes = 0) => new() { PlaylistTitle = "Stary album", TrackNumber = number, Folder = directory, Created = stamp.AddMinutes(minutes), State = "Gotowe" };
        var old = new[] { Legacy(1), Legacy(2), Legacy(1, 10), Legacy(2, 10), new Job { Title = "Bez albumu" } };
        var store = new Store(Path.Combine(directory, "legacy-groups")); store.Save(new Settings(), old);
        var upgraded = store.Load().Jobs;
        check(upgraded[0].PlaylistBatchId == upgraded[1].PlaylistBatchId && upgraded[2].PlaylistBatchId == upgraded[3].PlaylistBatchId && upgraded[0].PlaylistBatchId != upgraded[2].PlaylistBatchId && upgraded[4].PlaylistBatchId == "", "Version 1.1 history migration groups original batches without merging later same-title imports");
        store.Save(new Settings(), upgraded);
        check(store.Load().Jobs.Select(j => j.PlaylistBatchId).SequenceEqual(upgraded.Select(j => j.PlaylistBatchId)), "Legacy migration is stable on repeated startup");

        var additions = new MainViewModel(Path.Combine(directory, "batch-additions")) { Url = "https://example.com/album", Folder = directory };
        additions.ApplyInspection(new MediaInfo("Album", "", "2 pozycje", null, [], [
            new PlaylistEntry { Number = 1, Title = "Pierwszy", Url = "https://example.com/one.mp4", Available = true, Selected = true },
            new PlaylistEntry { Number = 2, Title = "Drugi", Url = "https://example.com/two.mp4", Available = true, Selected = true }
        ]));
        additions.AddPlaylist(false); additions.Quality = "256 kb/s"; additions.AddPlaylist(false);
        check(additions.QueueGroups.Count == 2 && additions.QueueGroups.All(g => g.Total == 2 && g.IsAlbum) && additions.Jobs.Select(j => j.PlaylistBatchId).Distinct().Count() == 2, "Each actual playlist addition creates its own durable batch");
        await additions.Stop();
    }
}
