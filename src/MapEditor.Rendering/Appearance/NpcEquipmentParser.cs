using System;
using System.Globalization;
using MapEditor.GameData.Rows;

namespace MapEditor.Rendering;

public readonly record struct NpcEquipmentSlot(int Id, RgbaValue Tint);

public readonly record struct NpcEquipment(
    NpcEquipmentSlot Chest,
    NpcEquipmentSlot Helm,
    NpcEquipmentSlot Legs,
    NpcEquipmentSlot Feet,
    NpcEquipmentSlot Shield,
    NpcEquipmentSlot Weapon);

public static class NpcEquipmentParser
{
    public const string DefaultEquippedItems = "0,*,0,*,0,*,0,*,0,*,0,*";

    private static readonly string[] SlotNames = { "chest", "head", "legs", "feet", "shield", "weapon" };

    public static bool TryParse(string? equippedItems, out NpcEquipment equipment, out string? diagnostic)
    {
        equipment = default;
        diagnostic = null;

        string[] tokens = (equippedItems ?? string.Empty).Split(',');
        NpcEquipmentSlot[] slots = new NpcEquipmentSlot[6];
        int i = 0;

        for (int slot = 0; slot < 6; slot++)
        {
            if (!TryParseSlot(tokens, slot, ref i, out slots[slot], out diagnostic))
            {
                return false;
            }
        }

        if (i < tokens.Length)
        {
            diagnostic = $"Equipped items '{equippedItems}' have {tokens.Length - i} extra token(s) after the weapon slot.";
            return false;
        }

        equipment = new NpcEquipment(slots[0], slots[1], slots[2], slots[3], slots[4], slots[5]);
        return true;
    }

    private static bool TryParseSlot(string[] tokens, int slot, ref int i, out NpcEquipmentSlot result, out string? diagnostic)
    {
        result = default;
        diagnostic = null;
        string name = SlotNames[slot];

        if (i >= tokens.Length || !TryParseId(tokens[i], out int id))
        {
            diagnostic = $"Equipped items slot {slot + 1} ({name}) has an invalid id '{(i < tokens.Length ? tokens[i] : string.Empty)}'.";
            i = Math.Min(i + 1, tokens.Length);
            return false;
        }

        i++;

        if (i >= tokens.Length)
        {
            diagnostic = $"Equipped items slot {slot + 1} ({name}) is missing its tint token.";
            return false;
        }

        if (tokens[i] == "*")
        {
            i++;
            result = new NpcEquipmentSlot(id, new RgbaValue(0, 0, 0, 0));
            return true;
        }

        if (i + 4 > tokens.Length
            || !TryParseByte(tokens[i], out int r)
            || !TryParseByte(tokens[i + 1], out int g)
            || !TryParseByte(tokens[i + 2], out int b)
            || !TryParseByte(tokens[i + 3], out int a))
        {
            diagnostic = $"Equipped items slot {slot + 1} ({name}) has an invalid tint; expected '*' or four byte values.";
            return false;
        }

        i += 4;
        result = new NpcEquipmentSlot(id, new RgbaValue(r, g, b, a));
        return true;
    }

    private static bool TryParseId(string token, out int value)
        => int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;

    private static bool TryParseByte(string token, out int value)
    {
        value = 0;
        return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value is >= 0 and <= 255;
    }
}
