using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalMusicHub.Data;
using LocalMusicHub.Models;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class AddToPlaylistPickerWindow
{
    private readonly LibraryRepository _repository;
    private readonly IReadOnlyList<LibraryPlaylist> _playlists;
    private readonly int _trackCount;

    public LibraryPlaylist? SelectedPlaylist { get; private set; }

    public AddToPlaylistPickerWindow(LibraryRepository repository, int trackCount)
    {
        HubTheme.Ensure(this);
        InitializeComponent();
        _repository = repository;
        _trackCount = trackCount;
        _playlists = repository.GetPlaylists(includeCoverArt: true)
            .Where(p => !p.IsSmart)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SubtitleText.Text = trackCount == 1 ? "1 track" : $"{trackCount} tracks";
        ApplyFilter("");
        if (PlaylistList.Items.Count > 0)
            PlaylistList.SelectedIndex = 0;
    }

    private void ApplyFilter(string search)
    {
        IEnumerable<LibraryPlaylist> filtered = _playlists;
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = _playlists.Where(p =>
                p.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        PlaylistList.ItemsSource = filtered.ToList();
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e) =>
        ApplyFilter(SearchBox.Text);

    private void PlaylistList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        ConfirmSelection();

    private void PlaylistList_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmSelection();
            e.Handled = true;
        }
    }

    private void Add_OnClick(object sender, RoutedEventArgs e) => ConfirmSelection();

    private void NewPlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        var prompt = new TextPromptWindow("New playlist", "Playlist name:") { Owner = this };
        if (prompt.ShowDialog() != true || string.IsNullOrWhiteSpace(prompt.Result))
            return;

        var created = _repository.CreatePlaylist(prompt.Result.Trim());
        SelectedPlaylist = created;
        DialogResult = true;
        Close();
    }

    private void ConfirmSelection()
    {
        if (PlaylistList.SelectedItem is not LibraryPlaylist playlist)
        {
            MessageBox.Show(this, "Select a playlist, or create a new one.", "Add to playlist",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedPlaylist = playlist;
        DialogResult = true;
        Close();
    }
}
