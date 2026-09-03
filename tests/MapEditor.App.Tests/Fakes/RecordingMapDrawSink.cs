using System;
using System.Collections.Generic;
using MapEditor.Rendering;

namespace MapEditor.App.Tests.Fakes;

public sealed class RecordingMapDrawSink : IMapDrawSink
{
    private readonly List<object> _calls = new();

    public IReadOnlyList<object> Calls => _calls;
    public int CallCount => _calls.Count;

    public void DrawSprite(in SpriteDrawOperation operation)
        => _calls.Add(operation);

    public void DrawPlaceholder(in PlaceholderDrawOperation operation)
        => _calls.Add(operation);

    public void DrawCellOverlay(in CellOverlayDrawOperation operation)
        => _calls.Add(operation);

    public void DrawGridLine(in GridLineDrawOperation operation)
        => _calls.Add(operation);
}
