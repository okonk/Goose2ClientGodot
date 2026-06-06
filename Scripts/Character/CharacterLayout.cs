using Goose2Client;

namespace Goose2Client.Character
{
    // Order MATCHES Unity AnimationSlot so (int)slot + 2 is the base draw order.
    public enum CharacterSlot { Mount = 0, Body, Eyes, Feet, Legs, Chest, Hair, Helm, Shield, Weapon }

    public static class CharacterLayout
    {
        // All 10 slots, back-to-front by base order, for iteration when (re)building a character.
        public static readonly CharacterSlot[] All =
        {
            CharacterSlot.Mount, CharacterSlot.Body, CharacterSlot.Eyes, CharacterSlot.Feet,
            CharacterSlot.Legs, CharacterSlot.Chest, CharacterSlot.Hair, CharacterSlot.Helm,
            CharacterSlot.Shield, CharacterSlot.Weapon,
        };

        /// <summary>Per-direction draw order (Unity Character.GetSortOrder). Base = (int)slot + 2;
        /// Shield/Weapon flip in front of / behind the body depending on facing. Higher = nearer.</summary>
        public static int SortOrder(CharacterSlot slot, Direction direction)
        {
            int order = (int)slot + 2;
            if (slot < CharacterSlot.Shield) return order;

            if (slot == CharacterSlot.Shield)
                return direction is Direction.Right or Direction.Up ? 0 : order;

            // Weapon
            return direction switch
            {
                Direction.Right => order,
                Direction.Down => order,
                Direction.Up => 1,
                Direction.Left => 0,
                _ => order,
            };
        }

        public static string TypeFolder(CharacterSlot slot) => slot switch
        {
            CharacterSlot.Body   => "Bodies",
            CharacterSlot.Mount  => "Bodies",   // mounts are just other bodies
            CharacterSlot.Hair   => "Hair",
            CharacterSlot.Eyes   => "Eyes",
            CharacterSlot.Chest  => "Chest",
            CharacterSlot.Helm   => "Helms",
            CharacterSlot.Legs   => "Legs",
            CharacterSlot.Feet   => "Feet",
            CharacterSlot.Shield => "Hands",    // shields & weapons render from Hands
            CharacterSlot.Weapon => "Hands",
            _ => "Bodies",
        };

        // Unity Character.SetUnderwear: male body 1 -> legs 3; female body 11 -> chest 8 + legs 4.
        // Every other body (monsters, NPCs) gets NO underwear. Returns 0 = "keep whatever is equipped".
        public static int UnderwearLegs(int bodyId, int equippedLegsId)
        {
            if (equippedLegsId != 0) return 0;
            if (bodyId == 1) return 3;    // male
            if (bodyId == 11) return 4;   // female
            return 0;
        }

        public static int UnderwearChest(int bodyId, int equippedChestId)
        {
            if (equippedChestId != 0) return 0;
            if (bodyId == 11) return 8;
            return 0;
        }
    }
}
