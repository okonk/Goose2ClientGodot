namespace MapEditor.App.ViewModels;

internal enum EditorClipboardKind
{
    Tiles,
    Spawn,
    Warp
}

internal sealed class EditorClipboardPayload
{
    private EditorClipboardPayload(
        EditorClipboardKind kind,
        TileClipboard? tiles,
        int? spawnNpcId,
        int? warpDestinationMapId,
        int? warpDestinationX,
        int? warpDestinationY,
        string? sourceSpreadsheetId)
    {
        Kind = kind;
        Tiles = tiles;
        SpawnNpcId = spawnNpcId;
        WarpDestinationMapId = warpDestinationMapId;
        WarpDestinationX = warpDestinationX;
        WarpDestinationY = warpDestinationY;
        SourceSpreadsheetId = sourceSpreadsheetId;
    }

    public EditorClipboardKind Kind { get; }

    public TileClipboard? Tiles { get; }

    public int? SpawnNpcId { get; }

    public int? WarpDestinationMapId { get; }

    public int? WarpDestinationX { get; }

    public int? WarpDestinationY { get; }

    public string? SourceSpreadsheetId { get; }

    public static EditorClipboardPayload FromTiles(TileClipboard tiles)
        => new(EditorClipboardKind.Tiles, tiles, null, null, null, null, null);

    public static EditorClipboardPayload FromSpawn(int npcId, string sourceSpreadsheetId)
        => new(EditorClipboardKind.Spawn, null, npcId, null, null, null, sourceSpreadsheetId);

    public static EditorClipboardPayload FromWarp(int destinationMapId, int destinationX, int destinationY, string sourceSpreadsheetId)
        => new(EditorClipboardKind.Warp, null, null, destinationMapId, destinationX, destinationY, sourceSpreadsheetId);
}
