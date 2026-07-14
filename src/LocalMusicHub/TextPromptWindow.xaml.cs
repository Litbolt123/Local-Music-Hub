using System.Windows;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class TextPromptWindow
{
    public string? Result { get; private set; }

    public TextPromptWindow(string title, string prompt, string initialValue = "")
    {
        HubTheme.Ensure(this);
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        var value = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            MessageBox.Show(this, "Please enter a name.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Result = value;
        DialogResult = true;
        Close();
    }
}
