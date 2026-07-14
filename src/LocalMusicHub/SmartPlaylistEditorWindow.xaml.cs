using System.Windows;
using System.Windows.Controls;
using LocalMusicHub.Data;
using LocalMusicHub.Models;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalMusicHub;

public partial class SmartPlaylistEditorWindow
{
    private readonly List<RuleRow> _rows = [];
    private readonly LibraryRepository? _repository;
    private readonly System.Windows.Threading.DispatcherTimer _previewTimer;
    private readonly IReadOnlyList<string> _artists;
    private readonly IReadOnlyList<string> _albums;
    private readonly IReadOnlyList<string> _genres;
    private readonly IReadOnlyList<string> _formats;

    public string? ResultName { get; private set; }
    public SmartPlaylistRules? ResultRules { get; private set; }

    public SmartPlaylistEditorWindow(
        string? name = null,
        SmartPlaylistRules? rules = null,
        bool isEdit = false,
        LibraryRepository? repository = null)
    {
        _repository = repository;
        _artists = repository?.GetDistinctArtists() ?? [];
        _albums = repository?.GetDistinctAlbums() ?? [];
        _genres = repository?.GetDistinctGenres() ?? [];
        _formats = repository?.GetDistinctFormats() ?? [];

        HubTheme.Ensure(this);
        InitializeComponent();
        TitleText.Text = isEdit ? "Edit smart playlist" : "New smart playlist";
        Title = isEdit ? "Edit smart playlist" : "New smart playlist";
        NameBox.Text = name ?? "";

        _previewTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            UpdateMatchCount();
        };

        if (rules is not null)
        {
            if (string.Equals(rules.MatchMode, "any", StringComparison.OrdinalIgnoreCase))
                MatchAnyRadio.IsChecked = true;
            else
                MatchAllRadio.IsChecked = true;
        }

        if (rules is { Rules.Count: > 0 })
        {
            foreach (var rule in rules.Rules)
                AddRuleRow(rule);
        }
        else
        {
            AddRuleRow();
        }

        UpdateMatchCount();
    }

    private void PresetHighlyRated_OnClick(object sender, RoutedEventArgs e) => ApplyPreset(SmartPlaylistRules.HighlyRated);
    private void PresetLast30_OnClick(object sender, RoutedEventArgs e) => ApplyPreset(SmartPlaylistRules.AddedLast30Days);
    private void PresetNeverPlayed_OnClick(object sender, RoutedEventArgs e) => ApplyPreset(SmartPlaylistRules.NeverPlayed);
    private void PresetRecentlyPlayed_OnClick(object sender, RoutedEventArgs e) => ApplyPreset(SmartPlaylistRules.RecentlyPlayed);

    private void ApplyPreset(SmartPlaylistRules preset)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameBox.Text = preset switch
            {
                _ when ReferenceEquals(preset, SmartPlaylistRules.HighlyRated) => "Highly rated",
                _ when ReferenceEquals(preset, SmartPlaylistRules.AddedLast30Days) => "Added last 30 days",
                _ when ReferenceEquals(preset, SmartPlaylistRules.NeverPlayed) => "Never played",
                _ when ReferenceEquals(preset, SmartPlaylistRules.RecentlyPlayed) => "Played recently",
                _ => NameBox.Text,
            };
        }

        ClearRules();
        foreach (var rule in preset.Rules)
            AddRuleRow(rule);
        SchedulePreview();
    }

    private void AddRule_OnClick(object sender, RoutedEventArgs e)
    {
        AddRuleRow();
        SchedulePreview();
    }

    private void MatchMode_OnChanged(object sender, RoutedEventArgs e) => SchedulePreview();
    private void RuleChanged_OnTextChanged(object sender, TextChangedEventArgs e) => SchedulePreview();

    private void SwitchToOr_OnClick(object sender, RoutedEventArgs e)
    {
        MatchAnyRadio.IsChecked = true;
        SchedulePreview();
    }

    private void ClearRules()
    {
        RulesPanel.Children.Clear();
        _rows.Clear();
    }

    private void SchedulePreview()
    {
        if (!IsLoaded)
            return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void UpdateMatchCount()
    {
        if (_repository is null)
        {
            MatchCountText.Text = "Matching tracks: (save to apply rules)";
            SwitchToOrButton.Visibility = Visibility.Collapsed;
            return;
        }

        if (!TryBuildRules(out var rules, out _))
        {
            MatchCountText.Text = "Matching tracks: fix rules to preview";
            SwitchToOrButton.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var count = _repository.CountTracksMatching(rules!);
            var conflict = DetectExclusiveAndConflict(rules!);
            SwitchToOrButton.Visibility = conflict is not null && count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (count == 0 && conflict is not null)
            {
                MatchCountText.Text =
                    $"Matching tracks: 0 — {conflict} can’t all be true together under All (AND). " +
                    "Switch to Any (OR) to include tracks from either.";
            }
            else if (count == 0)
            {
                MatchCountText.Text = "Matching tracks: 0 — try loosening a rule";
            }
            else
            {
                MatchCountText.Text = $"Matching tracks: {count:N0}";
            }
        }
        catch
        {
            MatchCountText.Text = "Matching tracks: —";
            SwitchToOrButton.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Detects rules like Album Is A + Album Is B under AND — impossible for one track.
    /// </summary>
    private static string? DetectExclusiveAndConflict(SmartPlaylistRules rules)
    {
        if (!string.Equals(rules.MatchMode, "all", StringComparison.OrdinalIgnoreCase))
            return null;

        var groups = rules.Rules
            .Where(r =>
                (r.Operator is "equals" or "is") &&
                !string.IsNullOrWhiteSpace(r.Value) &&
                r.Field is "album" or "artist" or "genre" or "title" or "format")
            .GroupBy(r => r.Field, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
            .ToList();

        if (groups.Count == 0)
            return null;

        var field = groups[0].Key switch
        {
            "album" => "Album Is …",
            "artist" => "Artist Is …",
            "genre" => "Genre Is …",
            "title" => "Title Is …",
            "format" => "Format Is …",
            _ => "these Is rules",
        };
        return field;
    }

    private void AddRuleRow(SmartPlaylistRule? seed = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var fieldBox = new WpfComboBox { Margin = new Thickness(0, 0, 6, 0), MinHeight = 30 };
        foreach (var field in RuleRow.FieldOptions)
            fieldBox.Items.Add(field);

        var opBox = new WpfComboBox { Margin = new Thickness(0, 0, 6, 0), MinHeight = 30 };

        var valueHost = new Grid { Margin = new Thickness(0, 0, 6, 0) };
        var valueBox = new WpfTextBox
        {
            Style = (Style)FindResource("HubTextBox"),
            ToolTip = "Enter a value for this rule",
        };
        valueBox.TextChanged += RuleChanged_OnTextChanged;

        var valueCombo = new WpfComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = true,
            StaysOpenOnEdit = true,
            MinHeight = 30,
            Visibility = Visibility.Collapsed,
            ToolTip = "Pick from your library or type to filter",
        };
        valueCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(RuleChanged_OnTextChanged));

        valueHost.Children.Add(valueBox);
        valueHost.Children.Add(valueCombo);

        var removeBtn = new WpfButton
        {
            Content = "✕",
            Style = (Style)FindResource("HubToolbarButton"),
            Padding = new Thickness(8, 4, 8, 4),
            ToolTip = "Remove rule",
        };

        Grid.SetColumn(fieldBox, 0);
        Grid.SetColumn(opBox, 1);
        Grid.SetColumn(valueHost, 2);
        Grid.SetColumn(removeBtn, 3);
        grid.Children.Add(fieldBox);
        grid.Children.Add(opBox);
        grid.Children.Add(valueHost);
        grid.Children.Add(removeBtn);

        var row = new RuleRow(fieldBox, opBox, valueBox, valueCombo, _artists, _albums, _genres, _formats);
        grid.Tag = row;
        _rows.Add(row);
        RulesPanel.Children.Add(grid);

        valueCombo.SelectionChanged += (_, _) =>
        {
            if (valueCombo.SelectedItem is string picked && !string.IsNullOrWhiteSpace(picked))
            {
                valueCombo.Text = picked;
                // Editable ComboBox often clears SelectedItem after Text is set — keep our own copy.
                row.RememberValue(picked);
            }
            SchedulePreview();
        };
        valueCombo.DropDownClosed += (_, _) =>
        {
            row.CommitValue();
            SchedulePreview();
        };
        valueCombo.LostFocus += (_, _) => row.CommitValue();

        fieldBox.SelectionChanged += (_, _) =>
        {
            row.SyncOperators();
            SchedulePreview();
        };
        opBox.SelectionChanged += (_, _) => SchedulePreview();
        removeBtn.Click += (_, _) =>
        {
            if (_rows.Count <= 1)
            {
                row.Reset();
                SchedulePreview();
                return;
            }

            _rows.Remove(row);
            RulesPanel.Children.Remove(grid);
            SchedulePreview();
        };

        if (seed is not null)
            row.Load(seed);
        else
            row.Reset();
    }

    private bool TryBuildRules(out SmartPlaylistRules? rules, out string error)
    {
        rules = null;
        error = "";
        foreach (var row in _rows)
            row.CommitValue();

        var built = new List<SmartPlaylistRule>();
        foreach (var row in _rows)
        {
            if (!row.TryBuild(out var rule, out error))
                return false;
            if (rule is not null)
                built.Add(rule);
        }

        if (built.Count == 0)
        {
            error = "Add at least one valid rule.";
            return false;
        }

        rules = new SmartPlaylistRules
        {
            MatchMode = MatchAnyRadio.IsChecked == true ? "any" : "all",
            Rules = built,
        };
        return true;
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Enter a playlist name.", "Smart playlist",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryBuildRules(out var rules, out var error))
        {
            MessageBox.Show(this, error, "Smart playlist", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ResultName = name;
        ResultRules = rules;
        DialogResult = true;
        Close();
    }

    private sealed class RuleRow
    {
        internal static readonly string[] FieldOptions =
        [
            "Rating", "Genre", "Artist", "Album", "Title",
            "Date added", "Last played", "Play count", "Year", "Format",
            "Never played", "Unrated", "Bitrate", "Duration",
        ];

        private readonly WpfComboBox _field;
        private readonly WpfComboBox _operator;
        private readonly WpfTextBox _valueBox;
        private readonly WpfComboBox _valueCombo;
        private readonly IReadOnlyList<string> _artists;
        private readonly IReadOnlyList<string> _albums;
        private readonly IReadOnlyList<string> _genres;
        private readonly IReadOnlyList<string> _formats;
        private string _rememberedValue = "";

        internal RuleRow(
            WpfComboBox field,
            WpfComboBox op,
            WpfTextBox valueBox,
            WpfComboBox valueCombo,
            IReadOnlyList<string> artists,
            IReadOnlyList<string> albums,
            IReadOnlyList<string> genres,
            IReadOnlyList<string> formats)
        {
            _field = field;
            _operator = op;
            _valueBox = valueBox;
            _valueCombo = valueCombo;
            _artists = artists;
            _albums = albums;
            _genres = genres;
            _formats = formats;
        }

        internal void RememberValue(string value) => _rememberedValue = value?.Trim() ?? "";

        internal void Reset()
        {
            _field.SelectedIndex = 0;
            SyncOperators();
            SetValueText("4");
        }

        internal void Load(SmartPlaylistRule rule)
        {
            _field.SelectedIndex = rule.Field.ToLowerInvariant() switch
            {
                "rating" => 0,
                "genre" => 1,
                "artist" => 2,
                "album" => 3,
                "title" => 4,
                "date_added" => 5,
                "last_played" => 6,
                "play_count" => 7,
                "year" => 8,
                "format" => 9,
                "never_played" => 10,
                "unrated" => 11,
                "bitrate" => 12,
                "duration" => 13,
                _ => 0,
            };
            SyncOperators();

            var opLabel = rule.Operator.ToLowerInvariant() switch
            {
                "min" => "At least",
                "max" => "At most",
                "equals" or "is" => "Is",
                "contains" => "Contains",
                "last_days" => "In last N days",
                "is_true" => "Is true",
                _ => null,
            };

            if (opLabel is not null)
            {
                for (var i = 0; i < _operator.Items.Count; i++)
                {
                    if (string.Equals(_operator.Items[i] as string, opLabel, StringComparison.Ordinal))
                    {
                        _operator.SelectedIndex = i;
                        break;
                    }
                }
            }
            else if (_operator.Items.Count > 0)
            {
                _operator.SelectedIndex = 0;
            }

            SetValueText(rule.Value);
            ApplyValueState();
        }

        internal void SyncOperators()
        {
            var field = _field.SelectedItem as string ?? "Rating";
            _operator.Items.Clear();
            switch (field)
            {
                case "Rating":
                    _operator.Items.Add("At least");
                    _operator.Items.Add("At most");
                    _operator.Items.Add("Is");
                    UseTextValue("1–5 stars");
                    if (string.IsNullOrWhiteSpace(GetValueText()) || !int.TryParse(GetValueText(), out _))
                        SetValueText("4");
                    break;
                case "Genre":
                    _operator.Items.Add("Is");
                    _operator.Items.Add("Contains");
                    UsePicker(_genres, "Pick a genre from your library");
                    break;
                case "Artist":
                    _operator.Items.Add("Is");
                    _operator.Items.Add("Contains");
                    UsePicker(_artists, "Pick an artist from your library");
                    break;
                case "Album":
                    _operator.Items.Add("Is");
                    _operator.Items.Add("Contains");
                    UsePicker(_albums, "Pick an album from your library");
                    break;
                case "Title":
                    _operator.Items.Add("Contains");
                    _operator.Items.Add("Is");
                    UseTextValue("Part of the song title");
                    break;
                case "Date added":
                case "Last played":
                    _operator.Items.Add("In last N days");
                    UseTextValue("e.g. 30");
                    if (string.IsNullOrWhiteSpace(GetValueText()) || !int.TryParse(GetValueText(), out _))
                        SetValueText(field == "Last played" ? "14" : "30");
                    break;
                case "Play count":
                    _operator.Items.Add("At least");
                    _operator.Items.Add("At most");
                    _operator.Items.Add("Is");
                    UseTextValue("e.g. 1");
                    if (string.IsNullOrWhiteSpace(GetValueText()))
                        SetValueText("1");
                    break;
                case "Year":
                    _operator.Items.Add("Is");
                    _operator.Items.Add("At least");
                    _operator.Items.Add("At most");
                    UseTextValue("e.g. 2020");
                    break;
                case "Format":
                    _operator.Items.Add("Is");
                    _operator.Items.Add("Contains");
                    UsePicker(_formats, "Pick a format from your library");
                    break;
                case "Never played":
                    _operator.Items.Add("Is true");
                    UseTextValue("(no value needed)");
                    SetValueText("");
                    break;
                case "Unrated":
                    _operator.Items.Add("Is true");
                    UseTextValue("(no value needed)");
                    SetValueText("");
                    break;
                case "Bitrate":
                    _operator.Items.Add("At least");
                    _operator.Items.Add("At most");
                    UseTextValue("kbps, e.g. 320");
                    if (string.IsNullOrWhiteSpace(GetValueText()) || !int.TryParse(GetValueText(), out _))
                        SetValueText("320");
                    break;
                case "Duration":
                    _operator.Items.Add("At least");
                    _operator.Items.Add("At most");
                    UseTextValue("seconds, e.g. 180");
                    if (string.IsNullOrWhiteSpace(GetValueText()) || !double.TryParse(GetValueText(), out _))
                        SetValueText("180");
                    break;
            }

            if (_operator.Items.Count > 0 && _operator.SelectedIndex < 0)
                _operator.SelectedIndex = 0;

            ApplyValueState();
        }

        private void UseTextValue(string tip)
        {
            _valueCombo.Visibility = Visibility.Collapsed;
            _valueBox.Visibility = Visibility.Visible;
            _valueBox.ToolTip = tip;
        }

        private void UsePicker(IReadOnlyList<string> options, string tip)
        {
            var current = GetValueText();
            // Don't carry over leftover values from Rating / other text fields.
            if (int.TryParse(current, out _) || current is "4" or "1" or "14" or "30")
                current = "";

            _valueBox.Visibility = Visibility.Collapsed;
            _valueCombo.Visibility = Visibility.Visible;
            _valueCombo.ToolTip = tip;
            _valueCombo.ItemsSource = options;
            _valueCombo.SelectedIndex = -1;
            _valueCombo.Text = "";
            _rememberedValue = "";

            if (!string.IsNullOrWhiteSpace(current))
            {
                var match = options.FirstOrDefault(o =>
                    string.Equals(o, current, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    _valueCombo.SelectedItem = match;
                    _valueCombo.Text = match;
                    _rememberedValue = match;
                }
                else
                {
                    _valueCombo.Text = current;
                    _rememberedValue = current;
                }
            }
        }

        private string GetValueText()
        {
            if (_valueCombo.Visibility == Visibility.Visible)
            {
                if (!string.IsNullOrWhiteSpace(_rememberedValue))
                    return _rememberedValue.Trim();

                if (_valueCombo.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
                    return selected.Trim();

                // Editable ComboBox often keeps the typed/selected text here even when SelectedItem is null.
                var text = _valueCombo.Text?.Trim() ?? "";
                if (text.Length > 0)
                    return text;

                // Last resort: read the editable textbox inside the combo template.
                if (_valueCombo.Template?.FindName("PART_EditableTextBox", _valueCombo) is WpfTextBox part &&
                    !string.IsNullOrWhiteSpace(part.Text))
                    return part.Text.Trim();

                return "";
            }

            return _valueBox.Text?.Trim() ?? "";
        }

        private void SetValueText(string value)
        {
            _valueBox.Text = value ?? "";
            _rememberedValue = value?.Trim() ?? "";
            if (_valueCombo.Visibility != Visibility.Visible)
            {
                _valueCombo.Text = value ?? "";
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                _valueCombo.SelectedIndex = -1;
                _valueCombo.Text = "";
                _rememberedValue = "";
                return;
            }

            var match = _valueCombo.Items.OfType<string>()
                .FirstOrDefault(i => string.Equals(i, value, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _valueCombo.SelectedItem = match;
                _valueCombo.Text = match;
                _rememberedValue = match;
            }
            else
            {
                _valueCombo.SelectedIndex = -1;
                _valueCombo.Text = value;
                _rememberedValue = value.Trim();
            }
        }

        /// <summary>Commit editable combo text before save/preview so SelectedItem and Text stay in sync.</summary>
        internal void CommitValue()
        {
            if (_valueCombo.Visibility != Visibility.Visible)
                return;

            var text = _valueCombo.Text?.Trim() ?? "";
            if (_valueCombo.Template?.FindName("PART_EditableTextBox", _valueCombo) is WpfTextBox part &&
                !string.IsNullOrWhiteSpace(part.Text))
                text = part.Text.Trim();

            if (string.IsNullOrWhiteSpace(text) && _valueCombo.SelectedItem is string selected)
                text = selected.Trim();

            if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(_rememberedValue))
                text = _rememberedValue.Trim();

            if (string.IsNullOrWhiteSpace(text))
                return;

            var match = _valueCombo.Items.OfType<string>()
                .FirstOrDefault(i => string.Equals(i, text, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _valueCombo.SelectedItem = match;
                _valueCombo.Text = match;
                _rememberedValue = match;
            }
            else
            {
                _valueCombo.Text = text;
                _rememberedValue = text;
            }
        }

        private void ApplyValueState()
        {
            var never = (_field.SelectedItem as string) is "Never played" or "Unrated";
            _valueBox.IsEnabled = !never;
            _valueCombo.IsEnabled = !never;
            if (never)
                SetValueText("");
        }

        internal bool TryBuild(out SmartPlaylistRule? rule, out string error)
        {
            rule = null;
            error = "";
            var field = _field.SelectedItem as string ?? "";
            var opLabel = _operator.SelectedItem as string ?? "";
            var value = GetValueText();

            switch (field)
            {
                case "Rating":
                    if (!int.TryParse(value, out var rating) || rating < 0 || rating > 5)
                    {
                        error = "Rating must be between 0 and 5.";
                        return false;
                    }

                    rule = new SmartPlaylistRule
                    {
                        Field = "rating",
                        Operator = opLabel switch
                        {
                            "At most" => "max",
                            "Is" => "equals",
                            _ => "min",
                        },
                        Value = rating.ToString(),
                    };
                    return true;

                case "Genre":
                case "Artist":
                case "Album":
                case "Title":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = field is "Artist" or "Album" or "Genre"
                            ? $"Pick or type a {field.ToLowerInvariant()} from your library."
                            : $"Enter text for {field.ToLowerInvariant()}.";
                        return false;
                    }

                    rule = new SmartPlaylistRule
                    {
                        Field = field.ToLowerInvariant(),
                        Operator = opLabel == "Is" ? "equals" : "contains",
                        Value = value,
                    };
                    return true;

                case "Date added":
                case "Last played":
                    if (!int.TryParse(value, out var days) || days < 1)
                    {
                        error = "Days must be a positive number.";
                        return false;
                    }

                    rule = new SmartPlaylistRule
                    {
                        Field = field == "Last played" ? "last_played" : "date_added",
                        Operator = "last_days",
                        Value = days.ToString(),
                    };
                    return true;

                case "Play count":
                    if (!int.TryParse(value, out var plays) || plays < 0)
                    {
                        error = "Play count must be zero or greater.";
                        return false;
                    }

                    rule = new SmartPlaylistRule
                    {
                        Field = "play_count",
                        Operator = opLabel switch
                        {
                            "At most" => "max",
                            "Is" => "equals",
                            _ => "min",
                        },
                        Value = plays.ToString(),
                    };
                    return true;

                case "Year":
                    if (!int.TryParse(value, out var year) || year < 0)
                    {
                        error = "Enter a valid year.";
                        return false;
                    }

                    rule = new SmartPlaylistRule
                    {
                        Field = "year",
                        Operator = opLabel switch
                        {
                            "At least" => "min",
                            "At most" => "max",
                            _ => "equals",
                        },
                        Value = year.ToString(),
                    };
                    return true;

                case "Format":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "Pick or type a format (e.g. FLAC, MP3).";
                        return false;
                    }

                    rule = new SmartPlaylistRule
                    {
                        Field = "format",
                        Operator = opLabel == "Contains" ? "contains" : "equals",
                        Value = value,
                    };
                    return true;

                case "Never played":
                    rule = new SmartPlaylistRule { Field = "never_played", Operator = "is_true", Value = "" };
                    return true;

                case "Unrated":
                    rule = new SmartPlaylistRule { Field = "unrated", Operator = "is_true", Value = "" };
                    return true;

                case "Bitrate":
                    if (!int.TryParse(value, out var bitrate) || bitrate < 0)
                    {
                        error = "Bitrate must be a non-negative kbps number.";
                        return false;
                    }

                    rule = new SmartPlaylistRule
                    {
                        Field = "bitrate",
                        Operator = opLabel == "At most" ? "max" : "min",
                        Value = bitrate.ToString(),
                    };
                    return true;

                case "Duration":
                    if (!double.TryParse(value, out var seconds) || seconds < 0)
                    {
                        error = "Duration must be seconds (0 or greater).";
                        return false;
                    }

                    rule = new SmartPlaylistRule
                    {
                        Field = "duration",
                        Operator = opLabel == "At most" ? "max" : "min",
                        Value = seconds.ToString("0.###"),
                    };
                    return true;

                default:
                    error = "Choose a rule field.";
                    return false;
            }
        }
    }
}
