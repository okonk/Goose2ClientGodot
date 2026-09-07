using System;
using System.Collections.Generic;
using MapEditor.Rendering;

namespace MapEditor.Rendering.Tests.Fakes;

public sealed class RecordingMapDrawSink : IMapDrawSink
{
    private readonly List<object> _calls = new();
    private Exception? _failure;
    private int _failOnCall = -1;

    public IReadOnlyList<object> Calls => _calls;

    public int CallCount => _calls.Count;

    public Exception? Failure
    {
        get => _failure;
        set => _failure = value;
    }

    public int FailOnCall
    {
        get => _failOnCall;
        set => _failOnCall = value;
    }

    public void DrawSprite(in SpriteDrawOperation operation) => Record(operation);

    public void DrawPlaceholder(in PlaceholderDrawOperation operation) => Record(operation);

    public void DrawCellOverlay(in CellOverlayDrawOperation operation) => Record(operation);

    public void DrawGridLine(in GridLineDrawOperation operation) => Record(operation);

    public void DrawGameDataMarker(in GameDataMarkerDrawOperation operation) => Record(operation);

    private void Record(object operation)
    {
        if (_failure is not null)
        {
            throw _failure;
        }

        if (_failOnCall >= 0 && _calls.Count >= _failOnCall)
        {
            throw new InvalidOperationException($"Fake sink failed before call {_calls.Count}.");
        }

        _calls.Add(operation);
    }
}
