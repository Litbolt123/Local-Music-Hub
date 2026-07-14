using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using LocalMusicHub.Data;
using LocalMusicHub.Models;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class DuplicatesWindow
{
    private readonly LibraryRepository _repository;
    private List<DuplicateGroup> _groups = [];

    public event EventHandler? LibraryChanged;

    public DuplicatesWindow(LibraryRepository repository)
    {
        _repository = repository;
        HubTheme.Ensure(this);
        InitializeComponent();
        Refresh();
    }

    public void Refresh()
    {
        _groups = _repository.FindDuplicateGroups().ToList();
        SummaryText.Text = _groups.Count == 0
            ? "No duplicate groups found (matched by title, artist, and duration)."
            : $"{_groups.Count} duplicate group{(_groups.Count == 1 ? "" : "s")} · keep-best prefers higher bitrate / lossless / rating.";
        GroupsList.ItemsSource = null;
        GroupsList.ItemsSource = _groups;
        TracksList.ItemsSource = null;
    }

    private void GroupsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupsList.SelectedItem is not DuplicateGroup group)
        {
            TracksList.ItemsSource = null;
            return;
        }

        var best = DuplicateScoring.PickBest(group.Tracks);
        var rows = group.Tracks
            .OrderByDescending(DuplicateScoring.Score)
            .ThenBy(t => t.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(t => new DuplicateRow
            {
                Track = t,
                IsBest = ReferenceEquals(t, best) ||
                         (t.Id > 0 && t.Id == best.Id) ||
                         string.Equals(t.FilePath, best.FilePath, StringComparison.OrdinalIgnoreCase),
            })
            .ToList();

        // Fix IsBest when PickBest returned different instance from list.
        var bestPath = best.FilePath;
        foreach (var row in rows)
            row.IsBest = string.Equals(row.Track.FilePath, bestPath, StringComparison.OrdinalIgnoreCase);

        TracksList.ItemsSource = rows;
        TracksList.SelectedItem = rows.FirstOrDefault(r => r.IsBest) ?? rows.FirstOrDefault();
    }

    private void TracksList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        /* selection drives Open / Delete */
    }

    private void OpenFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResolveSelectedTrack() is not { } track)
            return;

        var folder = Path.GetDirectoryName(track.FilePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void DeleteSelected_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResolveSelectedTrack() is not { } track)
            return;

        if (!ConfirmDelete([track], single: true))
            return;

        DeleteTracks([track]);
    }

    private void KeepBest_OnClick(object sender, RoutedEventArgs e)
    {
        if (GroupsList.SelectedItem is not DuplicateGroup group || group.Tracks.Count < 2)
        {
            MessageBox.Show(this, "Select a duplicate group with at least two files.", "Keep best",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var best = DuplicateScoring.PickBest(group.Tracks);
        var losers = group.Tracks
            .Where(t => !string.Equals(t.FilePath, best.FilePath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (losers.Count == 0)
            return;

        var confirm = MessageBox.Show(this,
            $"Keep:\n{best.FilePath}\n({DuplicateScoring.QualityLabel(best)})\n\nDelete {losers.Count} other file(s) in this group?",
            "Keep best in group",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        DeleteTracks(losers);
    }

    private void KeepBestAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (_groups.Count == 0)
            return;

        var toDelete = new List<LibraryTrack>();
        var keepCount = 0;
        foreach (var group in _groups)
        {
            if (group.Tracks.Count < 2)
                continue;
            var best = DuplicateScoring.PickBest(group.Tracks);
            keepCount++;
            toDelete.AddRange(group.Tracks.Where(t =>
                !string.Equals(t.FilePath, best.FilePath, StringComparison.OrdinalIgnoreCase)));
        }

        if (toDelete.Count == 0)
        {
            MessageBox.Show(this, "Nothing to delete.", "Keep best", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Across {keepCount} group(s), keep the best file and delete {toDelete.Count} duplicate(s).\n\nThis cannot be undone. Continue?",
            "Keep best in all groups",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        DeleteTracks(toDelete);
    }

    private LibraryTrack? ResolveSelectedTrack() =>
        TracksList.SelectedItem is DuplicateRow row ? row.Track :
        TracksList.SelectedItem as LibraryTrack;

    private bool ConfirmDelete(IReadOnlyList<LibraryTrack> tracks, bool single)
    {
        var msg = single
            ? $"Delete file from disk and library?\n{tracks[0].FilePath}"
            : $"Delete {tracks.Count} file(s) from disk and library?";
        return MessageBox.Show(this, msg, "Delete duplicate", MessageBoxButton.YesNo, MessageBoxImage.Warning) ==
               MessageBoxResult.Yes;
    }

    private void DeleteTracks(IReadOnlyList<LibraryTrack> tracks)
    {
        var errors = new List<string>();
        foreach (var track in tracks)
        {
            try
            {
                if (File.Exists(track.FilePath))
                    File.Delete(track.FilePath);
                _repository.RemovePath(track.FilePath);
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(track.FilePath)}: {ex.Message}");
            }
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
        Refresh();

        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", errors.Take(8)), "Some deletes failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private sealed class DuplicateRow
    {
        public required LibraryTrack Track { get; init; }
        public bool IsBest { get; set; }
        public string FilePath => Track.FilePath;
        public string QualityLine =>
            (IsBest ? "★ KEEP · " : "") + DuplicateScoring.QualityLabel(Track);
    }
}
