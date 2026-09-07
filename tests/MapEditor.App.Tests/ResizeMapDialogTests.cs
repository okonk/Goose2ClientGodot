using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Headless.XUnit;
using MapEditor.App.Dialogs;
using MapEditor.Core;
using MapEditor.GameData.Rows;
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

    private static Func<MapTileRectangle, MapResizePlan> PlanFor(MapDocument document, int spawns = 0, int warps = 0, int inbound = 0, bool pulled = false)
    {
        return window =>
        {
            int cropped = 0;
            for (int y = 0; y < document.Height; y++)
            {
                for (int x = 0; x < document.Width; x++)
                {
                    bool inside = x >= window.X && x < window.X + window.Width && y >= window.Y && y < window.Y + window.Height;
                    if (!inside && !document[x, y].IsEmpty)
                    {
                        cropped++;
                    }
                }
            }

            return new MapResizePlan(window, cropped, spawns, warps, inbound, pulled, Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        };
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
        ResizeMapDialog dialog = new(CreateDocument(10, 8), PlanFor(CreateDocument(10, 8)));
        dialog.WestBox.Text = "2";
        dialog.NorthBox.Text = "1";
        dialog.EastBox.Text = "3";
        dialog.SouthBox.Text = "4";

        MapTileRectangle? window = dialog.TryBuildWindow();

        Assert.Equal(new MapTileRectangle(-2, -1, 15, 13), window);
    }

    [AvaloniaFact]
    public void AbsoluteWidth_AdjustsEast()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8), PlanFor(CreateDocument(10, 8)));
        dialog.WestBox.Text = "2";

        dialog.ApplyWidth(10);

        Assert.Equal("-2", dialog.EastBox.Text);
        Assert.Equal("10", dialog.WidthBox.Text);
    }

    [AvaloniaFact]
    public void AbsoluteHeight_AdjustsSouth()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8), PlanFor(CreateDocument(10, 8)));
        dialog.NorthBox.Text = "1";

        dialog.ApplyHeight(8);

        Assert.Equal("-1", dialog.SouthBox.Text);
        Assert.Equal("8", dialog.HeightBox.Text);
    }

    [AvaloniaFact]
    public void TypedOffset_SyncsWidthBoxAndMatchesBuiltWindow()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8), PlanFor(CreateDocument(10, 8)));
        dialog.WestBox.Text = "2";
        dialog.WestBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));

        Assert.Equal("12", dialog.WidthBox.Text);
        MapTileRectangle? window = dialog.TryBuildWindow();
        Assert.NotNull(window);
        Assert.Equal(12, window.Value.Width);
    }

    [AvaloniaFact]
    public void TypedAbsoluteWidth_IsHonoredByBuiltWindow()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8), PlanFor(CreateDocument(10, 8)));
        dialog.WestBox.Text = "2";
        dialog.WestBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        dialog.WidthBox.Text = "12";
        dialog.WidthBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));

        MapTileRectangle? window = dialog.TryBuildWindow();
        Assert.NotNull(window);
        Assert.Equal(12, window.Value.Width);
    }

    [AvaloniaFact]
    public void DiscardReadout_ShowsThePlannedTileCount()
    {
        MapDocument document = CreateDocument(10, 8, fill: true);
        ResizeMapDialog dialog = new(document, PlanFor(document));
        dialog.WestBox.Text = "-10";
        dialog.WestBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        dialog.EastBox.Text = "5";
        dialog.EastBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));

        MapTileRectangle? window = dialog.TryBuildWindow();

        Assert.Equal(new MapTileRectangle(10, 0, 5, 8), window);
        Assert.True(dialog.DiscardText.IsVisible);
        Assert.Contains("80", dialog.DiscardText.Text);
    }

    [AvaloniaFact]
    public void DiscardReadout_IsHiddenWhenNothingIsCropped()
    {
        MapDocument document = CreateDocument(10, 8);
        for (int y = 2; y < 6; y++)
        {
            for (int x = 2; x < 7; x++)
            {
                document.SetLayer(x, y, 0, new MapTileLayer(1, 1));
            }
        }

        ResizeMapDialog dialog = new(document, PlanFor(document));
        CropBothAxes(dialog);

        Assert.NotNull(dialog.TryBuildWindow());
        Assert.False(dialog.DiscardText.IsVisible);
    }

    [AvaloniaFact]
    public void SheetReadout_ShowsCropCountsAndInboundWarningWhenPulled()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8), PlanFor(CreateDocument(10, 8), spawns: 3, warps: 1, inbound: 2, pulled: true));
        CropBothAxes(dialog);

        Assert.True(dialog.SheetText.IsVisible);
        Assert.Contains("3", dialog.SheetText.Text);
        Assert.Contains("1", dialog.SheetText.Text);
        Assert.True(dialog.InboundText.IsVisible);
        Assert.Contains("2", dialog.InboundText.Text);
    }

    [AvaloniaFact]
    public void SheetReadout_IsHiddenForAnUnpulledDocument()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8), PlanFor(CreateDocument(10, 8), spawns: 3, warps: 1, inbound: 2, pulled: false));
        CropBothAxes(dialog);

        Assert.False(dialog.SheetText.IsVisible);
        Assert.False(dialog.InboundText.IsVisible);
    }

    [AvaloniaFact]
    public void ResizeButton_DisabledOutsideDimensionBounds()
    {
        ResizeMapDialog dialog = new(CreateDocument(10, 8), PlanFor(CreateDocument(10, 8)));
        Assert.True(dialog.ResizeButton.IsEnabled);

        dialog.ApplyWidth(0);
        Assert.False(dialog.ResizeButton.IsEnabled);

        dialog.ApplyWidth(1001);
        Assert.False(dialog.ResizeButton.IsEnabled);

        dialog.ApplyWidth(10);
        Assert.True(dialog.ResizeButton.IsEnabled);
    }
}
