using System.Windows;
using System.Windows.Controls;
using LocalMusicHub.Services;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace LocalMusicHub;

public partial class EqualizerWindow
{
    private readonly Slider[] _sliders = new Slider[EqPresets.Frequencies.Length];

    public float[] ResultBands { get; private set; } = EqPresets.Flat.ToArray();

    public EqualizerWindow(IReadOnlyList<float>? initial = null)
    {
        HubTheme.Ensure(this);
        InitializeComponent();
        BuildBands(initial ?? EqPresets.Flat);
    }

    private void BuildBands(IReadOnlyList<float> gains)
    {
        BandsHost.Children.Clear();
        for (var i = 0; i < EqPresets.Frequencies.Length; i++)
        {
            var gain = i < gains.Count ? Math.Clamp(gains[i], -12f, 12f) : 0f;
            var panel = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
            var label = new TextBlock
            {
                Text = FormatFreq(EqPresets.Frequencies[i]),
                Style = (Style)FindResource("HubHintText"),
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
            };
            var slider = new Slider
            {
                Orientation = WpfOrientation.Vertical,
                Minimum = -12,
                Maximum = 12,
                Value = gain,
                Height = 200,
                TickFrequency = 1,
                IsSnapToTickEnabled = false,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
            };
            var value = new TextBlock
            {
                Text = $"{gain:0.#} dB",
                Style = (Style)FindResource("HubHintText"),
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
            };
            slider.ValueChanged += (_, _) => value.Text = $"{slider.Value:0.#} dB";
            _sliders[i] = slider;
            panel.Children.Add(label);
            panel.Children.Add(slider);
            panel.Children.Add(value);
            BandsHost.Children.Add(panel);
        }
    }

    private static string FormatFreq(float hz) =>
        hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";

    private void ApplyPreset(IReadOnlyList<float> gains)
    {
        for (var i = 0; i < _sliders.Length; i++)
            _sliders[i].Value = i < gains.Count ? gains[i] : 0;
    }

    private void Reset_OnClick(object sender, RoutedEventArgs e) => ApplyPreset(EqPresets.Flat);
    private void Bass_OnClick(object sender, RoutedEventArgs e) => ApplyPreset(EqPresets.BassBoost);
    private void Vocal_OnClick(object sender, RoutedEventArgs e) => ApplyPreset(EqPresets.Vocal);
    private void Treble_OnClick(object sender, RoutedEventArgs e) => ApplyPreset(EqPresets.Treble);

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        ResultBands = _sliders.Select(s => (float)s.Value).ToArray();
        DialogResult = true;
        Close();
    }
}
