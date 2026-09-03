namespace MapEditor.Core;

internal sealed class MapEditCommand
{
    internal const long BaseCommandBytes = 64;
    internal const long SegmentBytes = 32;
    internal const long LayerSlotBytes = 32;
    internal const long FlagsSlotBytes = 24;

    private MapEditCommand(MapEditChangeBuffer<MapLayerChange>? layerChanges, MapEditChangeBuffer<MapFlagsChange>? flagsChanges, int beforeStateId, int afterStateId)
    {
        LayerChanges = layerChanges;
        FlagsChanges = flagsChanges;
        BeforeStateId = beforeStateId;
        AfterStateId = afterStateId;
    }

    internal static MapEditCommand ForLayerChanges(MapEditChangeBuffer<MapLayerChange> changes, int beforeStateId, int afterStateId)
    {
        return new MapEditCommand(changes, null, beforeStateId, afterStateId);
    }

    internal static MapEditCommand ForFlagsChanges(MapEditChangeBuffer<MapFlagsChange> changes, int beforeStateId, int afterStateId)
    {
        return new MapEditCommand(null, changes, beforeStateId, afterStateId);
    }

    internal MapEditChangeBuffer<MapLayerChange>? LayerChanges { get; }

    internal MapEditChangeBuffer<MapFlagsChange>? FlagsChanges { get; }

    internal int BeforeStateId { get; }

    internal int AfterStateId { get; }

    internal long AccountedSizeBytes
    {
        get
        {
            if (LayerChanges is { } layerChanges)
            {
                return checked(BaseCommandBytes + layerChanges.SegmentCount * SegmentBytes + layerChanges.AllocatedSlotCount * LayerSlotBytes);
            }

            MapEditChangeBuffer<MapFlagsChange> flagsChanges = FlagsChanges!;
            return checked(BaseCommandBytes + flagsChanges.SegmentCount * SegmentBytes + flagsChanges.AllocatedSlotCount * FlagsSlotBytes);
        }
    }
}
