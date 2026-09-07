using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MapEditor.App.Dialogs;

internal partial class SpreadsheetDialog : Window
{
    public SpreadsheetDialog(string? prefill)
    {
        InitializeComponent();
        UrlBox.Text = prefill;
    }

    private void OnPull(object? sender, RoutedEventArgs e) => Close(UrlBox.Text);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
