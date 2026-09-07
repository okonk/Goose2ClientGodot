using System;

namespace MapEditor.App.ViewModels;

internal sealed class SharedTileClipboard
{
    private EditorClipboardPayload? _current;

    public EditorClipboardPayload? Current
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
