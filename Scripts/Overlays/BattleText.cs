using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.Overlays
{
    /// <summary>Container that holds active battle text lines above a character.
    /// Caps at 18 lines and uses a spread cycle for damage/heal numbers.</summary>
    public partial class BattleText : Node2D, IBridgedText
    {
        private int _position;
        private float _scale = 1f;

        public Character.Character AnchorOwner { get; set; }
        public Vector2 LocalOffsetWorld { get; set; }

        /// <summary>Local extent of the lines (the (0,−40) anchor is LocalOffsetWorld, NOT in here):
        /// line origins x ∈ [−4,12] (spread), y ∈ [−48,0] (y0 ∈ [−16,0] + rise ≤ 32 up); the 100×16
        /// label is centered on the origin; +4 outline → x ∈ [−58,66], y ∈ [−52,20] world units,
        /// scaled for culling (T3). Sized to the ACTUAL extent: bridge text is root-layer and is NOT
        /// clipped by the world blit, so an oversized rect would UNDER-cull — text stays visible in
        /// gutters where the world itself is cut off.
        /// (No Rect2 * float operator in GodotSharp — scale the components.)</summary>
        public Rect2 ScreenBounds => new Rect2(-58f * _scale, -52f * _scale, 124f * _scale, 72f * _scale);

        public void ApplyScale(float scale)
        {
            _scale = scale;
            for (int i = 0; i < GetChildCount(); i++)
                if (GetChild(i) is BattleTextLine line) line.ApplyScale(scale);
        }

        public void AddText(BattleTextType type, string text, int characterHeight, float scale)
        {
            // characterHeight currently unused — mirrors Unity; vertical placement handled by caller
            if (GetChildCount() >= 18) return;

            int childCount = GetChildCount();
            Vector2 offset = BattleTextLayout.ComputeSpreadOffset(type, childCount, ref _position);

            var (color, displayText) = BattleTextLayout.Resolve(type, text);

            var line = new BattleTextLine { Name = $"Line_{childCount}" };
            AddChild(line);
            line.Initialize(color, displayText, offset, scale);
        }
    }
}
