using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MapEditor.Core;

namespace MapEditor.App.Dialogs;

public partial class ResizeMapDialog : Window
{
    private readonly MapDocument _document;
    private readonly Func<MapTileRectangle, MapResizePlan> _plan;
    private readonly int _oldWidth;
    private readonly int _oldHeight;

    internal ResizeMapDialog(MapDocument document, Func<MapTileRectangle, MapResizePlan> plan)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
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

    internal MapTileRectangle? TryBuildWindow()
    {
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

        return new MapTileRectangle(-west, -north, width, height);
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
        if (TryBuildWindow() is { } window)
        {
            MapResizePlan plan = _plan(window);
            SizeText.Text = $"{_oldWidth} × {_oldHeight} → {window.Width} × {window.Height}";
            DiscardText.Text = $"⚠ Discards {plan.CroppedTiles} non-empty tiles";
            DiscardText.IsVisible = plan.CroppedTiles > 0;
            SheetText.Text = $"⚠ Crops {plan.CroppedSpawns} spawn and {plan.CroppedWarps} warp rows";
            SheetText.IsVisible = plan.HasPulledData && (plan.CroppedSpawns > 0 || plan.CroppedWarps > 0);
            InboundText.Text = $"⚠ {plan.InboundWarps} inbound warp destination(s) leave the map";
            InboundText.IsVisible = plan.HasPulledData && plan.InboundWarps > 0;
            ResizeButton.IsEnabled = true;
        }
        else
        {
            ResizeButton.IsEnabled = false;
        }
    }

    private void OnResizeClicked(object? sender, RoutedEventArgs e)
    {
        if (TryBuildWindow() is { } window)
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
