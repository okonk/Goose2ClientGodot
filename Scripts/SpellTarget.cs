using Godot;

namespace Goose2Client;

/// <summary>Spell targeting reticle — a white rectangle outline framing the target character.
/// Mirrors the Unity client's white-tinted, 9-sliced "spelltarget" sprite (sort order 1000):
/// the border stays a constant pixel width while the frame scales to the character.</summary>
public partial class SpellTarget : Node2D
{
    private const float BorderWidth = 2f;
    private Vector2 _size = new(Map.MapCoords.TileSize, Map.MapCoords.TileSize);

    public override void _Ready()
    {
        // High ZIndex so the frame draws above the character body (Unity sortingOrder 1000).
        ZIndex = 1000;
        QueueRedraw();
    }

    /// <summary>Size the frame to match the target character's dimensions (Unity ResizeTarget).</summary>
    public void ResizeTarget(int characterHeight)
    {
        if (characterHeight <= 0) characterHeight = 64;
        float ts = Map.MapCoords.TileSize;
        float h = Mathf.Max(1f, characterHeight / 32f);   // tile units — float, like Unity
        float w = Mathf.Max(1f, h * 0.75f);
        _size = new Vector2(w * ts, h * ts);
        // Center the frame on the character body. Unity places it at (h-1)*0.5 tiles above the
        // character root, but that root is the TILE CENTER; this port's character origin is the
        // TILE BOTTOM (feet), 0.5 tile lower — so we add that half-tile back: -h*0.5 tiles up.
        // (Equivalently -Height/2 px: the frame spans feet→head, matching the body sprite.)
        Position = new Vector2(0, -h * 0.5f * ts);
        QueueRedraw();
    }

    public override void _Draw()
    {
        // White rectangle outline centered on this node's origin. Constant border width (the node
        // itself is never scaled — the size is baked into the rect), matching Unity's 9-slice.
        var rect = new Rect2(-_size / 2f, _size);
        DrawRect(rect, Colors.White, filled: false, width: BorderWidth);
    }
}
