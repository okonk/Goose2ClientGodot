using Godot;

namespace Goose2Client.Overlays
{
    /// <summary>Character name label, rendered on the WorldTextBridge at native resolution.
    /// Visual constants are base × scale (font re-rasterizes at 12s px — crisp, T1); the anchor
    /// offset is world units (T7). Position/Visible are owned by the bridge — never set them here.</summary>
    public partial class BridgedNameLabel : Label, IBridgedText
    {
        private float _scale = 1f;

        public Character.Character AnchorOwner { get; set; }
        public Vector2 LocalOffsetWorld { get; set; }
        public Rect2 ScreenBounds => new Rect2(Vector2.Zero, Size);   // node origin = label top-left

        public void ApplyScale(float scale)
        {
            _scale = scale;
            AddThemeFontSizeOverride("font_size", Mathf.Max(1, Mathf.RoundToInt(12f * scale)));
            AddThemeConstantOverride("outline_size", Mathf.RoundToInt(4f * scale));
            Layout(AnchorOwner);
        }

        /// <summary>Re-measure at the current scale, size the label (screen px), and publish the
        /// world-unit anchor: centered horizontally, above the head (Height + NameTopOffset).
        /// Precondition: owner valid (may lack a body slot → Height 0). Called by ApplyScale (which
        /// Register runs BEFORE AddChild — metrics resolve off-tree) and by Character on text/height changes.</summary>
        public void Layout(Character.Character owner)
        {
            // IsInstanceValid: a QueueFreed character (OnEraseCharacter / ChangeMap) is still alive for
            // ~1 frame until the bridge's sweep frees this label; a scale change in that window would
            // dereference a freed GodotObject. Mirrors the bridge's own UpdateProjection guard.
            if (owner == null || !GodotObject.IsInstanceValid(owner)) return;
            // FONT METRICS, not GetMinimumSize(): min-size is stale same-frame after a
            // font-size-override change on a freshly-added label (probed headless) and Register
            // applies the scale before the label is in the tree. Same pattern as the bubble's
            // one-line measurement branch (ChatBubble.SetText). May differ ≤1-2px from Stage 1's
            // min-size-based layout — accepted; centering stays exact.
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
            LocalOffsetWorld = new Vector2(-w / (2f * _scale), -(bodyHeight + Character.Character.NameTopOffset));
        }
    }
}
