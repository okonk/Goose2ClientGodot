using Avalonia.Controls;

namespace MapEditor.App.Dialogs;

public partial class ErrorDialog : Window
{
    public ErrorDialog(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        OkButton.Click += (sender, e) => Close();
    }
}
