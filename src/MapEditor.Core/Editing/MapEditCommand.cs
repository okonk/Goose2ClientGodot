namespace MapEditor.Core;

internal abstract class MapEditCommand
{
    internal const long BaseCommandBytes = 64;
    internal const long SegmentBytes = 32;
    internal const long LayerSlotBytes = 32;
    internal const long FlagsSlotBytes = 24;

    protected MapEditCommand(int beforeStateId, int afterStateId)
    {
        BeforeStateId = beforeStateId;
        AfterStateId = afterStateId;
    }

    internal int BeforeStateId { get; }

    internal int AfterStateId { get; }

    internal abstract long AccountedSizeBytes { get; }

    internal abstract void Replay(MapDocument document, bool reverse, bool before);
}

internal abstract class MapDeltaCommand<T> : MapEditCommand
{
    protected MapDeltaCommand(MapEditChangeBuffer<T> changes, int beforeStateId, int afterStateId)
        : base(beforeStateId, afterStateId)
    {
        Changes = changes;
    }

    internal MapEditChangeBuffer<T> Changes { get; }

    protected abstract long SlotBytes { get; }

    protected abstract void Apply(MapDocument document, in T change, bool before);

    internal sealed override long AccountedSizeBytes
        => checked(BaseCommandBytes + Changes.SegmentCount * SegmentBytes + Changes.AllocatedSlotCount * SlotBytes);

    internal sealed override void Replay(MapDocument document, bool reverse, bool before)
    {
        for (int s = reverse ? Changes.SegmentCount - 1 : 0; reverse ? s >= 0 : s < Changes.SegmentCount; s += reverse ? -1 : 1)
        {
            T[] segment = Changes.GetSegment(s);
            int length = Changes.GetSegmentLength(s);
            for (int i = reverse ? length - 1 : 0; reverse ? i >= 0 : i < length; i += reverse ? -1 : 1)
            {
                Apply(document, in segment[i], before);
            }
        }
    }
}

internal sealed class MapLayerChangesCommand : MapDeltaCommand<MapLayerChange>
{
    public MapLayerChangesCommand(MapEditChangeBuffer<MapLayerChange> changes, int beforeStateId, int afterStateId)
        : base(changes, beforeStateId, afterStateId)
    {
    }

    protected override long SlotBytes => LayerSlotBytes;

    protected override void Apply(MapDocument document, in MapLayerChange change, bool before)
    {
        document.SetLayer(change.X, change.Y, change.LayerIndex, before ? change.Before : change.After);
    }
}

internal sealed class MapFlagsChangesCommand : MapDeltaCommand<MapFlagsChange>
{
    public MapFlagsChangesCommand(MapEditChangeBuffer<MapFlagsChange> changes, int beforeStateId, int afterStateId)
        : base(changes, beforeStateId, afterStateId)
    {
    }

    protected override long SlotBytes => FlagsSlotBytes;

    protected override void Apply(MapDocument document, in MapFlagsChange change, bool before)
    {
        document.SetFlags(change.X, change.Y, before ? change.BeforeFlags : change.AfterFlags);
    }
}
