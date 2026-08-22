using Godot;

namespace Goose2Client.Overlays
{
    /// <summary>Character name label on the WorldTextBridge. Position/Visible are bridge-owned — never set here.</summary>
    public partial class BridgedNameLabel : Label, IBridgedText
    {
        private float _scale = 1f;
        private float _worldScale = 1f;

        public Character.Character AnchorOwner { get; set; }
        public Vector2 LocalOffsetWorld { get; set; }
        public Rect2 ScreenBounds => new Rect2(Vector2.Zero, Size);   // node origin = label top-left

        public void ApplyScale(float textScale, float worldScale)
        {
            _scale = textScale;
            _worldScale = worldScale;
            AddThemeFontSizeOverride("font_size", Mathf.Max(1, Mathf.RoundToInt(12f * textScale)));
            AddThemeConstantOverride("outline_size", Mathf.RoundToInt(4f * textScale));
            Layout(AnchorOwner);
        }

        public void Layout(Character.Character owner)
        {
            // A QueueFreed character is still alive ~1 frame until the bridge sweep frees this label; a scale
            // change in that window would dereference a freed GodotObject (mirrors the bridge's guard).
            if (owner == null || !GodotObject.IsInstanceValid(owner)) return;
            // Font metrics, not GetMinimumSize(): min-size is stale same-frame after a font-size-override
            // change (probed), and Register applies the scale before the label is in the tree.
            var font = GetThemeFont("font") ?? GetThemeDefaultFont();
            int fontSize = Mathf.Max(1, Mathf.RoundToInt(12f * _scale));
            Vector2 natural = font.GetStringSize(Text, HorizontalAlignment.Left, -1, fontSize);
            float w = Mathf.Max(natural.X, 8f * _scale);
            float h = Mathf.Max(font.GetHeight(fontSize), 16f * _scale);
            Size = new Vector2(w, h);
            // 48 fallback mirrors RepositionOverlays' existing `Height <= 0 ? 48 : Height`
            // (Character.cs) — a character without a body slot must not anchor names to its feet.
            int bodyHeight = owner.Height <= 0 ? 48 : owner.Height;
            // Character.Character (not `Character` — from this namespace the bare name is the Goose2Client.Character namespace).
            // -w/(2·worldScale): screen-px half-width back in world units for centering.
            LocalOffsetWorld = new Vector2(-w / (2f * _worldScale), -(bodyHeight + Character.Character.NameTopOffset));
        }
    }
}
