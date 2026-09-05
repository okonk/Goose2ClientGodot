using System;

namespace MapEditor.App.ViewModels;

internal sealed class SharedTileClipboard
{
    private TileClipboard? _current;

    public TileClipboard? Current
    {
        get => _current;
        set
        {
            _current = value;
            Changed?.Invoke();
        }
    }

    public event Action? Changed;
}
