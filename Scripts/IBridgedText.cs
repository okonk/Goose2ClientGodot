using Godot;

namespace Goose2Client
{
    /// <summary>A text element rendered in the root viewport (child of <see cref="WorldTextBridge"/>)
    /// but anchored to a <see cref="Character.Character"/> in the world sub-viewport. The element owns
    /// its visual layout (fonts, sizes, internal offsets — in SCREEN px); the bridge owns the node's
    /// Position/Visible (per-frame projection + culling) and its lifetime (frees it when the owner dies).
    /// Element constraints: Control elements must be MouseFilter.Ignore (a Stop control in the root
    /// viewport would swallow world clicks) and anchor-free — the bridge writes an absolute Position.</summary>
    public interface IBridgedText
    {
        Character.Character Owner { get; set; }

        /// <summary>Anchor offset from the owner's origin (feet) in WORLD units (scaled once at projection).</summary>
        Vector2 LocalOffsetWorld { get; set; }

        /// <summary>Element's local screen-space rect, for culling (bridge offsets it by Position).</summary>
        Rect2 ScreenBounds { get; }

        /// <summary>Re-derive all visual constants at base × scale (font size, outline, paddings, label boxes).</summary>
        void ApplyScale(float scale);
    }
}
