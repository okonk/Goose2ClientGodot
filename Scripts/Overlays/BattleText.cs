using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.Overlays
{
    public partial class BattleText : Node2D, IBridgedText
    {
        private int _position;
        private float _scale = 1f;

        public Character.Character AnchorOwner { get; set; }
        public Vector2 LocalOffsetWorld { get; set; }

        /// Cull rect sized to the actual line extent: spread x∈[−4,12], worst case 18 stacked lines
        /// (17 × 16 push) + rise ≤ 32, 100×16 label, +4 outline → x∈[−58,66], y∈[−308,20], all × S —
        /// oversized would under-cull (root-layer text is not clipped by the world blit).
        public Rect2 ScreenBounds => new Rect2(-58f * _scale, -308f * _scale, 124f * _scale, 328f * _scale);

        public void ApplyScale(float textScale, float worldScale)
        {
            _scale = textScale;
            for (int i = 0; i < GetChildCount(); i++)
                if (GetChild(i) is BattleTextLine line) line.ApplyScale(textScale, worldScale);
        }

        public void AddText(BattleTextType type, string text, int characterHeight, float textScale, float worldScale)
        {
            if (GetChildCount() >= 18) return;

            int childCount = GetChildCount();
            Vector2 offset = BattleTextLayout.ComputeSpreadOffset(type, childCount, ref _position);

            // New line spawns at the anchor; shift existing lines up one line height so bursts stack
            // instead of overlapping (all lines rise at the same rate, so spacing is fixed once placed).
            for (int i = 0; i < childCount; i++)
                if (GetChild(i) is BattleTextLine existing) existing.PushUpOneLine();

            var (color, displayText) = BattleTextLayout.Resolve(type, text);

            var line = new BattleTextLine { Name = $"Line_{childCount}" };
            AddChild(line);
            line.Initialize(color, displayText, offset, textScale, worldScale);
        }
    }
}
