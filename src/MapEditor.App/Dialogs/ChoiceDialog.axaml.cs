using Avalonia.Controls;

namespace MapEditor.App.Dialogs;

public partial class ChoiceDialog : Window
{
    private readonly object? _tertiaryResult;
    private bool _completed;

    public ChoiceDialog(
        string title,
        string message,
        string primaryLabel,
        object? primaryResult,
        string secondaryLabel,
        object? secondaryResult,
        string? tertiaryLabel = null,
        object? tertiaryResult = null)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        _tertiaryResult = tertiaryResult;
        PrimaryButton.Content = primaryLabel;
        SecondaryButton.Content = secondaryLabel;
        PrimaryButton.Click += (sender, e) => Complete(primaryResult);
        SecondaryButton.Click += (sender, e) => Complete(secondaryResult);
        if (tertiaryLabel is not null)
        {
            TertiaryButton.Content = tertiaryLabel;
            TertiaryButton.Click += (sender, e) => Complete(tertiaryResult);
        }
        else
        {
            TertiaryButton.IsVisible = false;
        }
        // Closing via the window button instead of a choice maps to the tertiary (cancel) result;
        // ShowDialog<T> would otherwise return default(T), which is the primary (save) choice.
        Closing += (sender, e) =>
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            e.Cancel = true;
            Close(_tertiaryResult);
        };
    }

    private void Complete(object? result)
    {
        _completed = true;
        if (result is not null)
        {
            Close(result);
        }
        else
        {
            Close();
        }
    }
}
