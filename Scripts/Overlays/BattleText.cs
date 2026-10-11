using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.Overlays
{
    public partial class BattleText : Node2D, IBridgedText
    {
        private int _position;
        private float _scale = 1f;

        public Character.Character AnchorOwner { get; set; } = null!;
        public Vector2 LocalOffsetWorld { get; set; }

        /// Cull rect sized to the actual line extent: spread x∈[−4,12], worst case 18 stacked lines
        /// (17 × 16 push) + rise ≤ 48 (RiseCapPixels: 32 while accumulating + 16 after finalize),
        /// 100×16 label, +4 outline → x∈[−58,66], y∈[−324,20], all × S —
        /// oversized would under-cull (root-layer text is not clipped by the world blit).
        public Rect2 ScreenBounds => new Rect2(-58f * _scale, -324f * _scale, 124f * _scale, 344f * _scale);

        public void ApplyScale(float textScale, float worldScale)
        {
            _scale = textScale;
            for (int i = 0; i < GetChildCount(); i++)
                if (GetChild(i) is BattleTextLine line) line.ApplyScale(textScale, worldScale);
        }

        public BattleTextLine? AddText(BattleTextType type, string text, int characterHeight, float textScale, float worldScale, bool accumulating = false)
        {
            if (GetChildCount() >= 18) return null;

            int childCount = GetChildCount();
            Vector2 offset = BattleTextLayout.ComputeSpreadOffset(type, childCount, ref _position);

            // A new line spawns at the anchor. Push existing lines up only as far as needed to
            // clear it: simultaneous lines still stack (fixes overlap), but lines that have
            // already risen away are left alone so spaced-out healing stays compact.
            if (childCount > 0)
            {
                var ys = new float[childCount];
                var lines = new BattleTextLine[childCount];
                for (int i = 0; i < childCount; i++)
                {
                    lines[i] = (BattleTextLine)GetChild(i);
                    ys[i] = lines[i].CurrentScreenOffsetY();
                }

                var pushes = BattleTextLayout.PlanPushUps(ys, BattleTextLine.LabelHeight * textScale);
                for (int i = 0; i < childCount; i++)
                    if (pushes[i] > 0f) lines[i].PushUpScreenPixels(pushes[i]);
            }

            var (color, displayText) = BattleTextLayout.Resolve(type, text);

            var line = new BattleTextLine { Name = $"Line_{childCount}" };
            AddChild(line);
            line.Initialize(color, displayText, offset, textScale, worldScale, accumulating);
            return line;
        }
    }
}
