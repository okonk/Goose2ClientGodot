using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MapEditor.Core;

namespace MapEditor.App.Dialogs;

public partial class ResizeMapDialog : Window
{
    private readonly MapDocument _document;
    private readonly int _oldWidth;
    private readonly int _oldHeight;

    public ResizeMapDialog(MapDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _oldWidth = document.Width;
        _oldHeight = document.Height;
        InitializeComponent();
        WestBox.Text = "0";
        NorthBox.Text = "0";
        EastBox.Text = "0";
        SouthBox.Text = "0";
        WidthBox.Text = _oldWidth.ToString();
        HeightBox.Text = _oldHeight.ToString();
        ResizeButton.Click += OnResizeClicked;
        CancelButton.Click += OnCancelClicked;
        WestBox.TextChanged += OnOffsetChanged;
        NorthBox.TextChanged += OnOffsetChanged;
        EastBox.TextChanged += OnOffsetChanged;
        SouthBox.TextChanged += OnOffsetChanged;
        WidthBox.TextChanged += OnWidthChanged;
        HeightBox.TextChanged += OnHeightChanged;
        RefreshReadout();
    }

    internal MapTileRectangle? TryBuildWindow(out int discardedTiles)
    {
        discardedTiles = 0;
        if (!TryParseOffset(WestBox.Text, out int west) || !TryParseOffset(NorthBox.Text, out int north) ||
            !TryParseOffset(EastBox.Text, out int east) || !TryParseOffset(SouthBox.Text, out int south))
        {
            return null;
        }

        int width = _oldWidth + west + east;
        int height = _oldHeight + north + south;
        if (!IsDimension(width) || !IsDimension(height))
        {
            return null;
        }

        var window = new MapTileRectangle(-west, -north, width, height);
        discardedTiles = CountDiscarded(window);
        return window;
    }

    internal void ApplyWidth(int width)
    {
        if (TryParseOffset(WestBox.Text, out int west))
        {
            EastBox.Text = (width - _oldWidth - west).ToString();
        }

        WidthBox.Text = width.ToString();
        RefreshReadout();
    }

    internal void ApplyHeight(int height)
    {
        if (TryParseOffset(NorthBox.Text, out int north))
        {
            SouthBox.Text = (height - _oldHeight - north).ToString();
        }

        HeightBox.Text = height.ToString();
        RefreshReadout();
    }

    private void OnOffsetChanged(object? sender, TextChangedEventArgs e)
    {
        if (TryParseOffset(WestBox.Text, out int west) && TryParseOffset(EastBox.Text, out int east))
        {
            WidthBox.Text = (_oldWidth + west + east).ToString();
        }

        if (TryParseOffset(NorthBox.Text, out int north) && TryParseOffset(SouthBox.Text, out int south))
        {
            HeightBox.Text = (_oldHeight + north + south).ToString();
        }

        RefreshReadout();
    }

    private void OnWidthChanged(object? sender, TextChangedEventArgs e)
    {
        if (int.TryParse(WidthBox.Text, out int width))
        {
            ApplyWidth(width);
        }
        else
        {
            RefreshReadout();
        }
    }

    private void OnHeightChanged(object? sender, TextChangedEventArgs e)
    {
        if (int.TryParse(HeightBox.Text, out int height))
        {
            ApplyHeight(height);
        }
        else
        {
            RefreshReadout();
        }
    }

    private void RefreshReadout()
    {
        if (TryBuildWindow(out int discarded) is { } window)
        {
            SizeText.Text = $"{_oldWidth} × {_oldHeight} → {window.Width} × {window.Height}";
            DiscardText.Text = $"⚠ Discards {discarded} non-empty tiles";
            DiscardText.IsVisible = discarded > 0;
            ResizeButton.IsEnabled = true;
        }
        else
        {
            ResizeButton.IsEnabled = false;
        }
    }

    private int CountDiscarded(MapTileRectangle window)
    {
        if (window.ClipTo(_oldWidth, _oldHeight) is not { } keep)
        {
            return CountNonEmpty(0, 0, _oldWidth, _oldHeight);
        }

        // Disjoint bands: full-width top/bottom, then left/right restricted to the kept row span.
        return CountNonEmpty(0, 0, _oldWidth, keep.Y)
             + CountNonEmpty(0, keep.Y + keep.Height, _oldWidth, _oldHeight - (keep.Y + keep.Height))
             + CountNonEmpty(0, keep.Y, keep.X, keep.Height)
             + CountNonEmpty(keep.X + keep.Width, keep.Y, _oldWidth - (keep.X + keep.Width), keep.Height);
    }

    private int CountNonEmpty(int x, int y, int width, int height)
    {
        int count = 0;
        for (int tileY = y; tileY < y + height; tileY++)
        {
            for (int tileX = x; tileX < x + width; tileX++)
            {
                if (!_document[tileX, tileY].IsEmpty)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private void OnResizeClicked(object? sender, RoutedEventArgs e)
    {
        if (TryBuildWindow(out _) is { } window)
        {
            Close(window);
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static bool TryParseOffset(string? text, out int value)
        => int.TryParse(text, out value) && value >= -MapDocument.MaxDimension && value <= MapDocument.MaxDimension;

    private static bool IsDimension(int value)
        => value >= MapDocument.MinDimension && value <= MapDocument.MaxDimension;
}
