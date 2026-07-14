using System.Windows;

namespace LocalMusicHub;

public partial class LibraryToolsWindow
{
    private readonly Action _onStats;
    private readonly Action _onDuplicates;
    private readonly Action _onOrganize;
    private readonly Action _onReplayGain;
    private readonly Action _onCleanDead;
    private readonly Action _onScanLibrary;

    public LibraryToolsWindow(
        int trackCount,
        Action onStats,
        Action onDuplicates,
        Action onOrganize,
        Action onReplayGain,
        Action onCleanDead,
        Action onScanLibrary)
    {
        InitializeComponent();
        _onStats = onStats;
        _onDuplicates = onDuplicates;
        _onOrganize = onOrganize;
        _onReplayGain = onReplayGain;
        _onCleanDead = onCleanDead;
        _onScanLibrary = onScanLibrary;
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

    private void ScanLibrary_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
        _onScanLibrary();
    }
}
