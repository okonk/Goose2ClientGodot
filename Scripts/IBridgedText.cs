using Godot;

namespace Goose2Client
{
    /// <summary>Text element in the root viewport, anchored to a world <see cref="Character.Character"/>. Constraint:
    /// Controls must be MouseFilter.Ignore (a Stop control would swallow world clicks) and anchor-free — the bridge writes Position.</summary>
    public interface IBridgedText
    {
        Character.Character AnchorOwner { get; set; }

        /// Anchor offset from the owner's feet — world units, scaled once at projection.
        Vector2 LocalOffsetWorld { get; set; }

        Rect2 ScreenBounds { get; }

        /// textScale (UI factor) sizes the text; worldScale converts world-unit offsets to screen px.
        void ApplyScale(float textScale, float worldScale);
    }
}
