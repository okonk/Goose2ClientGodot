using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.Overlays
{
    /// <summary>Pure logic for battle text layout: spread offsets and color/text resolution.
    /// Mirrors Unity BattleText.AddText / BattleTextLine.Create behaviour.</summary>
    public static class BattleTextLayout
    {
        private static readonly System.Collections.Generic.HashSet<BattleTextType> SpreadTypes =
            new() { BattleTextType.Red1, BattleTextType.Red2, BattleTextType.Red4,
                    BattleTextType.Red5, BattleTextType.Green7, BattleTextType.Green8 };

        /// <summary>Compute the pixel offset for a new battle text line.
        /// For spread types, cycles through a 9-position grid and pushes upward as more lines accumulate.
        /// For non-spread types, returns (0,0) and does not modify position.
        /// Y is negated for Godot convention (up = negative Y).</summary>
        public static Vector2 ComputeSpreadOffset(BattleTextType type, int childCount, ref int position)
        {
            if (!SpreadTypes.Contains(type))
                return new Vector2(0, 0);

            // Update position counter
            if (childCount != 0)
                position = (position + 1) % 9;
            else
                position = 0;

            // Compute y offset (Unity-positive, will negate for Godot)
            int y = Mathf.Min(childCount / 3, 2) * 8;

            // Compute x offset
            int x;
            if (position % 3 != 0)
                x = (position % 3 != 1) ? 12 : -4;
            else
                x = 4;

            // Godot Y is down; negate the upward push
            return new Vector2(x, -y);
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
