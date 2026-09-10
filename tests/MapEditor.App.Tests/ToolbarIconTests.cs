using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace MapEditor.App.Tests;

public class ToolbarIconTests
{
    private static readonly string[] ToolNames =
    {
        "SelectTool", "MultiSelectTool", "EyedropperTool",
        "PencilTool", "EraserTool", "TerrainTool", "FloodFillTool", "BlockedTool",
        "SpawnTool", "WarpTool"
    };

    private static Path IconOf(MainWindowHarness harness, string name)
    {
        ToggleButton button = harness.Window.GetVisualDescendants()
            .OfType<ToggleButton>().First(b => b.Name == name);
        return button.GetVisualDescendants().OfType<Path>().Single();
    }

    [AvaloniaFact]
    public void ToolbarIcon_TerrainToolUsesUniqueIconTerrain()
    {
        using var harness = MainWindowHarness.Create();
        Geometry terrain = IconOf(harness, "TerrainTool").Data!;

        Assert.Equal(
            Geometry.Parse("M3 18 L8 13 L12 16 L17 9 L21 13 M3 21 H21 M7 9 A2 2 0 1 1 11 9 A2 2 0 1 1 7 9").ToString(),
            terrain.ToString());
        Assert.DoesNotContain(ToolNames.Where(name => name != "TerrainTool"),
            name => ReferenceEquals(terrain, IconOf(harness, name).Data));
    }

    [AvaloniaFact]
    public void Toolbar_ToolsUseStrokedIconPaths()
    {
        using var harness = MainWindowHarness.Create();
        foreach (string name in ToolNames)
        {
            Path path = IconOf(harness, name);
            Assert.NotNull(path.Stroke);
            Assert.NotNull(path.Data);
        }
    }

    // The icons are authored on a shared 24x24 grid. A sub-path that starts with a relative
    // move continues from the previous sub-path's end point and lands off the grid, so
    // checking the bounds catches that class of mistake.
    [AvaloniaFact]
    public void Toolbar_IconsFitTheSharedGrid()
    {
        using var harness = MainWindowHarness.Create();
        foreach (string name in ToolNames)
        {
            var bounds = IconOf(harness, name).Data!.Bounds;
            Assert.True(bounds.X >= 0 && bounds.Y >= 0, $"{name} starts outside the grid: {bounds}");
            Assert.True(bounds.Right <= 24 && bounds.Bottom <= 24, $"{name} overflows the grid: {bounds}");
            Assert.True(bounds.Width >= 12 && bounds.Height >= 12, $"{name} is too small for the grid: {bounds}");
        }
    }

    [AvaloniaFact]
    public void Toolbar_EachToolHasItsOwnIcon()
    {
        using var harness = MainWindowHarness.Create();
        List<object> geometries = ToolNames.Select(n => (object)IconOf(harness, n).Data!).ToList();
        Assert.Equal(geometries.Count, geometries.Distinct().Count());
    }

    // The stroke follows the button's Foreground, so the checked tool has to read differently
    // from the idle ones; equal strokes would mean the control theme's states never applied.
    [AvaloniaFact]
    public void Toolbar_CheckedToolIconIsDistinctFromIdleTools()
    {
        using var harness = MainWindowHarness.Create();
        Assert.True(harness.Window.GetVisualDescendants().OfType<ToggleButton>().First(b => b.Name == "PencilTool").IsChecked);

        Assert.NotEqual(
            IconOf(harness, "PencilTool").Stroke!.ToString(),
            IconOf(harness, "EraserTool").Stroke!.ToString());
    }
}
