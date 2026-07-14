using System.Windows;
using LocalMusicHub.Data;
using LocalMusicHub.Models;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class OrganizePreviewWindow
{
    private readonly LibraryRepository _repository;
    private readonly IReadOnlyList<LibraryTrack> _tracks;
    private readonly LibraryFolderWatcher _folderWatcher;
    private readonly PlaybackService _playback;
    private List<OrganizeRow> _rows = [];

    public event EventHandler? LibraryChanged;

    public OrganizePreviewWindow(
        LibraryRepository repository,
        IReadOnlyList<LibraryTrack> tracks,
        LibraryFolderWatcher folderWatcher,
        PlaybackService playback)
    {
        _repository = repository;
        _tracks = tracks;
        _folderWatcher = folderWatcher;
        _playback = playback;
        HubTheme.Ensure(this);
        InitializeComponent();
        TemplateBox.Text = App.Settings.OrganizeTemplate;
        RootBox.Text = App.Settings.OrganizeRoot
            ?? App.Settings.LibraryFolders.FirstOrDefault()
            ?? AppPaths.DefaultMusicFolder;
        RefreshPreview();
    }

    private void Refresh_OnClick(object sender, RoutedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        var template = TemplateBox.Text.Trim();
        var root = RootBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(root))
        {
            SummaryText.Text = "Enter a template and root folder.";
            PreviewList.ItemsSource = null;
            return;
        }

        _rows = _tracks.Select(track =>
        {
            var target = FileOrganizerService.BuildTargetPath(template, track, root);
            if (!string.Equals(target, track.FilePath, StringComparison.OrdinalIgnoreCase))
                target = FileOrganizerService.ResolveCollision(target);
            return new OrganizeRow
            {
                Track = track,
                OldPath = track.FilePath,
                NewPath = target,
                Apply = !string.Equals(target, track.FilePath, StringComparison.OrdinalIgnoreCase),
            };
        }).ToList();

        var moving = _rows.Count(r => r.Apply);
        SummaryText.Text = moving == 0
            ? "No files need to move for the current template."
            : $"{moving} of {_rows.Count} track{(moving == 1 ? "" : "s")} will move.";
        PreviewList.ItemsSource = _rows;
    }

    private async void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(r => r.Apply).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "No rows selected to apply.", "Organize files",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        App.Settings.OrganizeTemplate = TemplateBox.Text.Trim();
        App.Settings.OrganizeRoot = RootBox.Text.Trim();
        App.SaveSettings();

        _folderWatcher.SuppressEvents = true;
        try
        {
            var done = 0;
            var failed = 0;
            await Task.Run(() =>
            {
                foreach (var row in selected)
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(row.NewPath);
                        if (!string.IsNullOrWhiteSpace(dir))
                            Directory.CreateDirectory(dir);

                        (bool WasPlaying, TimeSpan Position)? resume = null;
                        var released = _playback.ReleaseCurrentFileIfMatches(row.OldPath);
                        if (released is { } state)
                            resume = (state.WasPlaying, state.Position);

                        if (File.Exists(row.OldPath))
                            File.Move(row.OldPath, row.NewPath);

                        Dispatcher.Invoke(() => _repository.UpdateFilePath(row.Track.Id, row.NewPath));

                        if (resume is { } playState)
                            Dispatcher.Invoke(() => _playback.ResumeAfterFileRelease(playState.WasPlaying, playState.Position));

                        done++;
                    }
                    catch
                    {
                        failed++;
                    }
                }
            }).ConfigureAwait(true);

            LibraryChanged?.Invoke(this, EventArgs.Empty);
            MessageBox.Show(this,
                failed == 0
                    ? $"Moved {done} file{(done == 1 ? "" : "s")}."
                    : $"Moved {done}; {failed} failed.",
                "Organize files",
                MessageBoxButton.OK,
                failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            DialogResult = true;
            Close();
        }
        finally
        {
            _folderWatcher.SuppressEvents = false;
        }
    }

    private sealed class OrganizeRow
    {
        public required LibraryTrack Track { get; init; }
        public required string OldPath { get; init; }
        public required string NewPath { get; init; }
        public bool Apply { get; set; }
    }
}
