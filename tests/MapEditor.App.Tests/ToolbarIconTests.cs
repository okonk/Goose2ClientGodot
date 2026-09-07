using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace MapEditor.App.Tests;

public class ToolbarIconTests
{
    private static readonly string[] ToolNames =
    {
        "SelectTool", "MultiSelectTool", "EyedropperTool",
        "PencilTool", "EraserTool", "FloodFillTool", "BlockedTool",
        "SpawnTool", "WarpTool"
    };

    private static Path IconOf(MainWindowHarness harness, string name)
    {
        ToggleButton button = harness.Window.GetVisualDescendants()
            .OfType<ToggleButton>().First(b => b.Name == name);
        return button.GetVisualDescendants().OfType<Path>().Single();
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
