using System.Collections.Generic;
using Goose2Client;

namespace Goose2Client.Character
{
    public static class AnimationNames
    {
        public static string DirectionString(Direction d) => d switch
        {
            Direction.Up => "up",
            Direction.Right => "right",
            Direction.Down => "down",
            Direction.Left => "left",
            _ => "down",
        };

        public static string Clip(string state, Direction d) => $"{state}-{DirectionString(d)}";

        /// <summary>Server BodyState -> attack-clip weapon variant. 4=1hand, 5=staff, 6=2hand,
        /// 7=bow; anything else (incl. 3 = unarmed) is the no-equip swing.</summary>
        public static string AttackVariant(int bodyState) => bodyState switch
        {
            4 => "1hand",
            5 => "staff",
            6 => "2hand",
            7 => "bow",
            _ => "no-equip",
        };

        /// <summary>Ordered candidate clip names (most specific first) for a motion state, given the
        /// character's BodyState (3 = unarmed/no-equip, otherwise a weapon is equipped). Slots carry
        /// different clip sets (a weapon only has -equip / attack-&lt;type&gt; clips, hair only idle/walk),
        /// so the caller plays the first candidate the slot's SpriteFrames actually contains.</summary>
        public static IReadOnlyList<string> Candidates(string motion, int bodyState, Direction d)
        {
            bool equipped = bodyState != 3;
            List<string> bases = motion switch
            {
                "idle" => equipped ? new List<string> { "idle-equip", "idle", "idle-no-equip" }
                                   : new List<string> { "idle-no-equip", "idle" },
                "walk" => equipped ? new List<string> { "walk-equip", "walk", "walk-no-equip" }
                                   : new List<string> { "walk-no-equip", "walk" },
                "attack" => equipped
                    ? new List<string> { $"attack-{AttackVariant(bodyState)}", "attack-1hand", "attack", "attack-no-equip", "idle-equip", "idle" }
                    : new List<string> { "attack-no-equip", "attack", "idle-no-equip", "idle" },
                "cast" => equipped
                    ? new List<string> { "cast", "idle-equip", "idle" }
                    : new List<string> { "cast", "idle-no-equip", "idle" },
                "mounted-idle" => new List<string> { "mounted-idle", "idle-equip", "idle" },
                "mounted-walk" => new List<string> { "mounted-walk", "walk-equip", "walk" },
                _ => new List<string> { motion },
            };

            string dir = DirectionString(d);
            var result = new List<string>(bases.Count);
            foreach (var b in bases) result.Add($"{b}-{dir}");
            return result;
        }
    }
}
