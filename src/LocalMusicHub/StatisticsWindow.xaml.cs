using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using LocalMusicHub.Models;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class StatisticsWindow
{
    private readonly LibraryStatistics _stats;

    public StatisticsWindow(LibraryStatistics stats)
    {
        _stats = stats;
        HubTheme.Ensure(this);
        InitializeComponent();
        Render(stats);
    }

    private void ExportCsv_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export library statistics",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"library-stats-{DateTime.Now:yyyyMMdd}.csv",
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("section,label,value");
            WriteRow(sb, "Overview", "Tracks", _stats.TrackCount.ToString());
            WriteRow(sb, "Overview", "Albums", _stats.AlbumCount.ToString());
            WriteRow(sb, "Overview", "Artists", _stats.ArtistCount.ToString());
            WriteRow(sb, "Overview", "Total duration (seconds)", ((int)_stats.TotalDuration.TotalSeconds).ToString());
            WriteRow(sb, "Overview", "Never played", _stats.NeverPlayedCount.ToString());
            WriteRow(sb, "Overview", "Added last 30 days", _stats.RecentlyAddedCount.ToString());
            foreach (var item in _stats.TopArtistsByPlays)
                WriteRow(sb, "Top artists", item.Name, item.Count.ToString());
            foreach (var item in _stats.TopTracksByPlays)
                WriteRow(sb, "Top tracks", $"{item.Title} · {item.Artist}", item.PlayCount.ToString());
            foreach (var item in _stats.FormatBreakdown)
                WriteRow(sb, "Formats", item.Label, item.Count.ToString());
            foreach (var item in _stats.RatingBreakdown)
                WriteRow(sb, "Ratings", item.Label, item.Count.ToString());

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show(this, $"Exported to:\n{dlg.FileName}", "Export CSV",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void WriteRow(StringBuilder sb, string section, string label, string value)
    {
        static string Esc(string s) =>
            s.Contains('"') || s.Contains(',') || s.Contains('\n')
                ? $"\"{s.Replace("\"", "\"\"")}\""
                : s;

        sb.Append(Esc(section)).Append(',').Append(Esc(label)).Append(',').Append(Esc(value)).AppendLine();
    }

    private void Render(LibraryStatistics stats)
    {
        AddSection("Overview");
        AddMetric("Tracks", stats.TrackCount.ToString("N0"));
        AddMetric("Albums", stats.AlbumCount.ToString("N0"));
        AddMetric("Artists", stats.ArtistCount.ToString("N0"));
        AddMetric("Total duration", FormatDuration(stats.TotalDuration));
        AddMetric("Never played", stats.NeverPlayedCount.ToString("N0"));
        AddMetric("Added last 30 days", stats.RecentlyAddedCount.ToString("N0"));

        AddSection("Top artists by plays");
        foreach (var item in stats.TopArtistsByPlays)
            AddMetric(item.Name, item.Count.ToString("N0"));

        AddSection("Top tracks by plays");
        foreach (var item in stats.TopTracksByPlays)
            AddMetric($"{item.Title} · {item.Artist}", item.PlayCount.ToString("N0"));

        AddSection("Formats");
        foreach (var item in stats.FormatBreakdown)
            AddMetric(item.Label, item.Count.ToString("N0"));

        AddSection("Ratings");
        foreach (var item in stats.RatingBreakdown)
        {
            var label = item.Label == "0" ? "Unrated" : new string('★', int.Parse(item.Label));
            AddMetric(label, item.Count.ToString("N0"));
        }

        var hint = new TextBlock
        {
            Style = (Style)FindResource("HubHintText"),
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Text = "Tip: create smart playlists for Never played or Highly rated (4+) tracks.",
        };
        ContentPanel.Children.Add(hint);
    }

    private void AddSection(string title)
    {
        ContentPanel.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)FindResource("HubSettingLabel"),
            Margin = new Thickness(0, 14, 0, 6),
        });
    }

    private void AddMetric(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        var name = new TextBlock
        {
            Text = label,
            Style = (Style)FindResource("HubBodyText"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0),
        };
        var count = new TextBlock
        {
            Text = value,
            Style = (Style)FindResource("HubBodyText"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
        };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(count, 1);
        grid.Children.Add(name);
        grid.Children.Add(count);
        ContentPanel.Children.Add(grid);
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
}
