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
        /// so the caller plays the first candidate the slot's SpriteFrames actually contains.
        /// Missing clips resolve to blank (hidden) — never cross-motion fallbacks like attack→idle.
        /// Idle/walk still offer equip / no-equip variants of the *same* motion so Hands sheets draw.</summary>
        public static IReadOnlyList<string> Candidates(string motion, int bodyState, Direction d)
        {
            bool equipped = bodyState != 3;
            // Idle/walk end unarmed lists with -equip: Hands weapon/shield sheets only ship
            // idle-equip / walk-equip / attack-<type>. Attack/cast never fall back to idle —
            // Unity substitutes Blank (invisible) when that motion's clip is absent.
            List<string> bases = motion switch
            {
                "idle" => equipped ? new List<string> { "idle-equip", "idle", "idle-no-equip" }
                                   : new List<string> { "idle-no-equip", "idle", "idle-equip" },
                "walk" => equipped ? new List<string> { "walk-equip", "walk", "walk-no-equip" }
                                   : new List<string> { "walk-no-equip", "walk", "walk-equip" },
                // Attack-family only. No idle: shields without attack art hide for the swing.
                "attack" => AttackCandidates(equipped, bodyState),
                // Cast only — Hands never ship cast clips; ResolveClip blanks them (Unity Blank).
                "cast" => new List<string> { "cast" },
                // Mounted poses: only real mounted-* clips. Hands/shields never ship mounted
                // sheets — Unity substitutes Blank rather than foot-walk art.
                "mounted-idle" => new List<string> { "mounted-idle" },
                "mounted-walk" => new List<string> { "mounted-walk" },
                _ => new List<string> { motion },
            };

            string dir = DirectionString(d);
            var result = new List<string>(bases.Count);
            foreach (var b in bases) result.Add($"{b}-{dir}");
            return result;
        }

        /// <summary>Attack-family clip bases only (no idle). Prefer the weapon variant, then
        /// 1hand (when not already preferred), then generic attack / no-equip.</summary>
        private static List<string> AttackCandidates(bool equipped, int bodyState)
        {
            if (!equipped)
                return new List<string> { "attack-no-equip", "attack" };

            string variant = AttackVariant(bodyState);
            var list = new List<string> { $"attack-{variant}" };
            if (variant != "1hand")
                list.Add("attack-1hand");
            list.Add("attack");
            list.Add("attack-no-equip");
            return list;
        }
    }
}
