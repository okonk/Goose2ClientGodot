using System;
using Avalonia.Headless.XUnit;
using Xunit;

namespace MapEditor.App.Tests;

public class MainWindowDefaultLayoutTests : IDisposable
{
    private readonly MainWindowHarness _harness = MainWindowHarness.Create();

    public void Dispose() => _harness.Dispose();

    [AvaloniaFact]
    public void Palette_DefaultsToTenColumnsWide()
    {
        MainWindow window = _harness.Window;
        window.UpdateLayout();

        Assert.Equal(10, window.Palette.Columns);
    }
}
