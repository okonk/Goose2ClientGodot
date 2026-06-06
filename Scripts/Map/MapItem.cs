using Godot;

namespace Goose2Client.Map;

/// <summary>A dropped item on the ground. Sprite anchored bottom-center; tint via Modulate
/// (replaces Unity's material _Tint). Tooltip/interaction is Step 7/8.</summary>
public partial class MapItem : Sprite2D
{
    public void Setup(AtlasTexture tex, int tileX, int tileY, Color tint)
    {
        Texture = tex;
        Centered = false;
        var size = tex.GetSize();
        var anchor = MapCoords.TileBottomCenter(tileX, tileY);
        Position = new Vector2(anchor.X - size.X / 2f, anchor.Y - size.Y);
        if (tint.A > 0) Modulate = tint;     // RGBA all-0 sentinel ⇒ no tint (MapObjectPacket '*' case)
    }
}
