using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.Overlays
{
    /// <summary>Container that holds active battle text lines above a character.
    /// Caps at 18 lines and uses a spread cycle for damage/heal numbers.</summary>
    public partial class BattleText : Node2D
    {
        private int _position;

        public void AddText(BattleTextType type, string text, int characterHeight)
        {
            // characterHeight currently unused — mirrors Unity; vertical placement handled by caller
            if (GetChildCount() >= 18) return;

            int childCount = GetChildCount();
            Vector2 offset = BattleTextLayout.ComputeSpreadOffset(type, childCount, ref _position);

            var (color, displayText) = BattleTextLayout.Resolve(type, text);

            var line = new BattleTextLine { Name = $"Line_{childCount}" };
            AddChild(line);
            line.Initialize(color, displayText, offset);
        }
    }
}
