using Avalonia;
using Avalonia.Markup.Xaml;

namespace MapEditor.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
