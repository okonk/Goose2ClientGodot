namespace MapEditor.Core;

internal sealed class MapEditCommand
{
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
}
