using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(MapEditor.App.Tests.HeadlessTestAppBuilder))]

namespace MapEditor.App.Tests;

public class HeadlessTestApplication : Application
{
    public HeadlessTestApplication()
    {
        Styles.Add(new FluentTheme());
        // Same styles the real app loads, so tests see the shipped theme.
        Styles.Add(new StyleInclude(new System.Uri("avares://MapEditor.App/"))
        {
            Source = new System.Uri("avares://MapEditor.App/Styles/EditorTheme.axaml")
        });
    }
}

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<HeadlessTestApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
