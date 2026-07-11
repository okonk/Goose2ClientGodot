using Godot;

namespace Goose2Client.Map;

/// <summary>A dropped item on the ground. Sprite anchored bottom-center; tint via shared
/// TintMaterial lerp shader (port of Unity material _Tint). Carries ItemStats for hover tooltip.</summary>
public partial class MapItem : Sprite2D
{
    /// <summary>Item data for the hover tooltip (set by MapManager after Setup).</summary>
    public ItemStats Item { get; set; }

    public void Setup(AtlasTexture tex, int tileX, int tileY, Color tint)
    {
        Texture = tex;
        Centered = false;
        var size = tex.GetSize();
        var anchor = MapCoords.TileBottomCenter(tileX, tileY);
        Position = new Vector2(anchor.X - size.X / 2f, anchor.Y - size.Y);
        if (tint.A > 0) Material = TintMaterial.Make(tint);   // Unity lerp-tint shader, NOT Modulate

        // Area2D for hover tooltip — covers the sprite rect.
        // Sprite is Centered=false with top-left at (0,0), so the rect is (0,0)..size.
        // RectangleShape2D is centered on the CollisionShape2D position, so place it at size/2.
        var area = new Area2D { InputPickable = true, Name = "HoverArea" };
        AddChild(area);
        var shape = new CollisionShape2D();
        shape.Shape = new RectangleShape2D { Size = size };
        shape.Position = size / 2f;
        area.AddChild(shape);

        area.MouseEntered += () =>
        {
            if (Item != null)
                Goose2Client.UI.TooltipManager.Instance?.ShowMapItemTooltip(Item, this);
        };
        area.MouseExited += () =>
        {
            if (Item != null)
                Goose2Client.UI.TooltipManager.Instance?.HideMapItemTooltipIfMatching(Item);
        };
    }
}
