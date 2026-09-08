namespace MapEditor.Rendering;

public interface IMapDrawSink
{
    void DrawSprite(in SpriteDrawOperation operation);

    void DrawPlaceholder(in PlaceholderDrawOperation operation);

    void DrawCellOverlay(in CellOverlayDrawOperation operation);

    void DrawGridLine(in GridLineDrawOperation operation);

    void DrawGameDataMarker(in GameDataMarkerDrawOperation operation);

    void DrawNpcImage(in NpcImageDrawOperation operation);

    void DrawNpcPartPlaceholder(in NpcPartPlaceholderDrawOperation operation);

    void DrawNpcSpawnAnchor(in NpcSpawnAnchorDrawOperation operation);

    void DrawNpcName(in NpcNameDrawOperation operation);
}
