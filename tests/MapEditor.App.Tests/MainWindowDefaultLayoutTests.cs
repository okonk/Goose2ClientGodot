using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace MapEditor.App.Tests;

public class MainWindowDefaultLayoutTests : IDisposable
{
    private readonly MainWindowHarness _harness = MainWindowHarness.Create();

    public void Dispose() => _harness.Dispose();

    [AvaloniaFact]
    public void Toolbar_OrderIncludesTerrainBetweenEraserAndFloodFill()
    {
        string[] order = _harness.Window.FindControl<Border>("Toolbar")!.GetVisualDescendants()
            .OfType<ToggleButton>().Select(button => button.Name!).ToArray();
        int terrain = Array.IndexOf(order, "TerrainTool");

        Assert.Equal("EraserTool", order[terrain - 1]);
        Assert.Equal("FloodFillTool", order[terrain + 1]);
    }

    [AvaloniaFact]
    public void Palette_DefaultsToTenColumnsWide()
    {
        MainWindow window = _harness.Window;
        window.UpdateLayout();

        Assert.Equal(10, window.Palette.Columns);
    }
}
