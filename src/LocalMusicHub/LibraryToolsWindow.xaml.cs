using System.Windows;
using LocalMusicHub.Services;

namespace LocalMusicHub;

public partial class LibraryToolsWindow
{
    private readonly Action _onStats;
    private readonly Action _onDuplicates;
    private readonly Action _onOrganize;
    private readonly Action _onReplayGain;
    private readonly Action _onCleanDead;
    private readonly Action _onFixWindowsDetails;
    private readonly Action _onScanLibrary;
    private readonly Action _onScanFolders;

    public LibraryToolsWindow(
        int trackCount,
        Action onStats,
        Action onDuplicates,
        Action onOrganize,
        Action onReplayGain,
        Action onCleanDead,
        Action onFixWindowsDetails,
        Action onScanLibrary,
        Action onScanFolders)
    {
        HubTheme.Ensure(this);
        InitializeComponent();
        _onStats = onStats;
        _onDuplicates = onDuplicates;
        _onOrganize = onOrganize;
        _onReplayGain = onReplayGain;
        _onCleanDead = onCleanDead;
        _onFixWindowsDetails = onFixWindowsDetails;
        _onScanLibrary = onScanLibrary;
        _onScanFolders = onScanFolders;
        SummaryText.Text = $"{trackCount:N0} tracks indexed · pick a tool below";
    }

    private void Stats_OnClick(object sender, RoutedEventArgs e)
    {
        _onStats();
    }

    private void Duplicates_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
        _onDuplicates();
    }

    private void Organize_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
        _onOrganize();
    }

    private void ReplayGain_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
        _onReplayGain();
    }

    private void CleanDead_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
        _onCleanDead();
    }

    private void FixWindowsDetails_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
        _onFixWindowsDetails();
    }

    private void ScanLibrary_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
        _onScanLibrary();
    }

    private void ScanFolders_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
        _onScanFolders();
    }
}
