using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.Overlays
{
    /// <summary>Pure logic for battle text layout: x jitter and color/text resolution.
    /// Color/text mirrors Unity BattleTextLine.Create; x jitter keeps the reference {4,-4,12}
    /// cycle but drops the 3-row y grid (8 px rows can't separate 16 px lines) — vertical
    /// spacing is instead handled by BattleText pushing existing lines up per new line.</summary>
    public static class BattleTextLayout
    {
        private static readonly System.Collections.Generic.HashSet<BattleTextType> SpreadTypes =
            new() { BattleTextType.Red1, BattleTextType.Red2, BattleTextType.Red4,
                    BattleTextType.Red5, BattleTextType.Green7, BattleTextType.Green8 };

        /// <summary>Compute the x jitter for a new battle text line.
        /// Spread types cycle through 3 x positions; y is always 0 (new lines spawn at the
        /// anchor and existing lines are pushed up to make room).
        /// Non-spread types return (0,0) and do not modify position.</summary>
        public static Vector2 ComputeSpreadOffset(BattleTextType type, int childCount, ref int position)
        {
            if (!SpreadTypes.Contains(type))
                return new Vector2(0, 0);

            if (childCount != 0)
                position = (position + 1) % 3;
            else
                position = 0;

            int x = position switch
            {
                0 => 4,
                1 => -4,
                _ => 12,
            };

            return new Vector2(x, 0);
        }

        /// <summary>Resolve the display color and text for a battle text type.
        /// Status-effect types override the text to their keyword (STUNNED, ROOTED, DODGE, MISS).</summary>
        public static (Color color, string text) Resolve(BattleTextType type, string text)
        {
            return type switch
            {
                BattleTextType.Red1 or BattleTextType.Red2 or BattleTextType.Red4 or
                BattleTextType.Red5 or BattleTextType.Red61 =>
                    (Color.Color8(154, 0, 0), text),

                BattleTextType.Green7 or BattleTextType.Green8 =>
                    (Color.Color8(136, 204, 64), text),

                BattleTextType.Yellow60 =>
                    (Color.Color8(248, 208, 0), text),

                BattleTextType.Stunned10 or BattleTextType.Stunned50 =>
                    (Colors.White, "STUNNED"),

                BattleTextType.Rooted11 or BattleTextType.Rooted51 =>
                    (Colors.White, "ROOTED"),

                BattleTextType.Dodge20 =>
                    (Colors.White, "DODGE"),

                BattleTextType.Miss21 =>
                    (Colors.White, "MISS"),

                _ => // White and any unknown type
                    (Colors.White, text),
            };
        }
    }
}
