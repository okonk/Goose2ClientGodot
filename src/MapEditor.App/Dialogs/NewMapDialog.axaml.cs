using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MapEditor.Core;

namespace MapEditor.App.Dialogs;

public partial class NewMapDialog : Window
{
    public NewMapDialog()
    {
        InitializeComponent();
        WidthBox.Text = MapDocument.DefaultWidth.ToString();
        HeightBox.Text = MapDocument.DefaultHeight.ToString();
        OkButton.Click += OnOkClicked;
        CancelButton.Click += OnCancelClicked;
        Opened += (sender, e) => WidthBox.Focus();
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        if (!TryParseDimension(WidthBox.Text, out int width) || !TryParseDimension(HeightBox.Text, out int height))
        {
            ErrorText.Text = $"Dimensions must be whole numbers between {MapDocument.MinDimension} and {MapDocument.MaxDimension}.";
            ErrorText.IsVisible = true;
            return;
        }

        Close(new NewMapRequest(width, height));
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static bool TryParseDimension(string? text, out int value)
        => int.TryParse(text, out value) && value >= MapDocument.MinDimension && value <= MapDocument.MaxDimension;
}
