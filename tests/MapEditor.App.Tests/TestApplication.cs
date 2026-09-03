using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(MapEditor.App.Tests.HeadlessTestAppBuilder))]

namespace MapEditor.App.Tests;

public class HeadlessTestApplication : Application
{
    public HeadlessTestApplication()
    {
        Styles.Add(new FluentTheme());
    }
}

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<HeadlessTestApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
