using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Headless.XUnit;
using MapEditor.App.Dialogs;
using MapEditor.Core;
using Xunit;

namespace MapEditor.App.Tests;

public class ResizeMapDialogTests
{
    private static MapDocument CreateDocument(int width, int height, bool fill = false)
    {
        MapDocument document = MapDocument.Create(width, height);
        if (fill)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    document.SetLayer(x, y, 0, new MapTileLayer(1, 1));
                }
            }
        }

        return document;
    }

    private static void CropBothAxes(ResizeMapDialog dialog)
    {
        dialog.WestBox.Text = "-2";
        dialog.EastBox.Text = "-3";
        dialog.NorthBox.Text = "-2";
        dialog.SouthBox.Text = "-2";
    }

    [AvaloniaFact]
    public void Offsets_ConvertToWindow()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8));
        dialog.WestBox.Text = "2";
        dialog.NorthBox.Text = "1";
        dialog.EastBox.Text = "3";
        dialog.SouthBox.Text = "4";

        MapTileRectangle? window = dialog.TryBuildWindow(out _);

        Assert.Equal(new MapTileRectangle(-2, -1, 15, 13), window);
    }

    [AvaloniaFact]
    public void AbsoluteWidth_AdjustsEast()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8));
        dialog.WestBox.Text = "2";

        dialog.ApplyWidth(10);

        Assert.Equal("-2", dialog.EastBox.Text);
        Assert.Equal("10", dialog.WidthBox.Text);
    }

    [AvaloniaFact]
    public void AbsoluteHeight_AdjustsSouth()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8));
        dialog.NorthBox.Text = "1";

        dialog.ApplyHeight(8);

        Assert.Equal("-1", dialog.SouthBox.Text);
        Assert.Equal("8", dialog.HeightBox.Text);
    }

    [AvaloniaFact]
    public void TypedOffset_SyncsWidthBoxAndMatchesBuiltWindow()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8));
        dialog.WestBox.Text = "2";
        dialog.WestBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));

        Assert.Equal("12", dialog.WidthBox.Text);
        MapTileRectangle? window = dialog.TryBuildWindow(out _);
        Assert.NotNull(window);
        Assert.Equal(12, window.Value.Width);
    }

    [AvaloniaFact]
    public void TypedAbsoluteWidth_IsHonoredByBuiltWindow()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8));
        dialog.WestBox.Text = "2";
        dialog.WestBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        dialog.WidthBox.Text = "12";
        dialog.WidthBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));

        MapTileRectangle? window = dialog.TryBuildWindow(out _);
        Assert.NotNull(window);
        Assert.Equal(12, window.Value.Width);
    }

    [AvaloniaFact]
    public void DiscardCount_IsWholeMapWhenWindowIsEntirelyOutside()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8, fill: true));
        dialog.WestBox.Text = "-10";
        dialog.EastBox.Text = "5";

        MapTileRectangle? window = dialog.TryBuildWindow(out int discarded);

        Assert.Equal(new MapTileRectangle(10, 0, 5, 8), window);
        Assert.Equal(80, discarded);
    }

    [AvaloniaFact]
    public void DiscardCount_CountsEachTileOnce_WithCornerOverlap()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8, fill: true));
        CropBothAxes(dialog);

        MapTileRectangle? window = dialog.TryBuildWindow(out int discarded);

        Assert.Equal(new MapTileRectangle(2, 2, 5, 4), window);
        Assert.Equal(60, discarded);
    }

    [AvaloniaFact]
    public void DiscardCount_IsZeroForEmptyBorder()
    {
        MapDocument document = CreateDocument(10, 8);
        for (int y = 2; y < 6; y++)
        {
            for (int x = 2; x < 7; x++)
            {
                document.SetLayer(x, y, 0, new MapTileLayer(1, 1));
            }
        }

        ResizeMapDialog dialog = new(document);
        CropBothAxes(dialog);

        MapTileRectangle? window = dialog.TryBuildWindow(out int discarded);

        Assert.NotNull(window);
        Assert.Equal(0, discarded);
    }

    [AvaloniaFact]
    public void ResizeButton_DisabledOutsideDimensionBounds()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8));
        Assert.True(dialog.ResizeButton.IsEnabled);

        dialog.ApplyWidth(0);
        Assert.False(dialog.ResizeButton.IsEnabled);

        dialog.ApplyWidth(1001);
        Assert.False(dialog.ResizeButton.IsEnabled);

        dialog.ApplyWidth(10);
        Assert.True(dialog.ResizeButton.IsEnabled);
    }
}
