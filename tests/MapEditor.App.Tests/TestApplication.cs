using Avalonia;
using Avalonia.Markup.Xaml;

namespace MapEditor.App.Tests;

public class TestApplication : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
