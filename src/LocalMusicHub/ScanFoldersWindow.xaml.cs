using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class ScanFoldersWindow
{
    private readonly ObservableCollection<ScanFolderItem> _folders = [];

    public IReadOnlyList<string> SelectedFolders { get; private set; } = [];

    public ScanFoldersWindow(IEnumerable<string> libraryFolders)
    {
        HubTheme.Ensure(this);
        InitializeComponent();

        foreach (var folder in libraryFolders
                     .Where(f => !string.IsNullOrWhiteSpace(f))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(folder))
                _folders.Add(new ScanFolderItem(folder, isSelected: true));
        }

        FoldersList.ItemsSource = _folders;
    }

    private void AddFolder_OnClick(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a folder to scan for music",
            UseDescriptionForTitle = true,
        };

        var last = _folders.LastOrDefault(f => f.IsSelected)?.Path
                   ?? _folders.LastOrDefault()?.Path;
        if (!string.IsNullOrWhiteSpace(last) && Directory.Exists(last))
            dlg.SelectedPath = last;
        else if (Directory.Exists(AppPaths.DefaultMusicFolder))
            dlg.SelectedPath = AppPaths.DefaultMusicFolder;

        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        var path = Path.GetFullPath(dlg.SelectedPath);
        var existing = _folders.FirstOrDefault(f =>
            string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsSelected = true;
            return;
        }

        _folders.Add(new ScanFolderItem(path, isSelected: true));
    }

    private void SelectAll_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var folder in _folders)
            folder.IsSelected = true;
    }

    private void SelectNone_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var folder in _folders)
            folder.IsSelected = false;
    }

    private void Scan_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedFolders = _folders
            .Where(f => f.IsSelected && Directory.Exists(f.Path))
            .Select(f => f.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (SelectedFolders.Count == 0)
        {
            MessageBox.Show(this, "Select at least one folder to scan.", "Scan folders",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
        Close();
    }

    private sealed class ScanFolderItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public ScanFolderItem(string path, bool isSelected)
        {
            Path = path;
            _isSelected = isSelected;
        }

        public string Path { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
