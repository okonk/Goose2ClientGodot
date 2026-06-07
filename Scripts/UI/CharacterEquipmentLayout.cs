using Godot;

namespace Goose2Client.UI;

/// <summary>
/// Anatomical equipment-slot layout for the Character window (400×222 panel).
/// Mirrors the Unity CharacterCanvas.prefab slot positions.
/// Positions are taken from the Unity serialized <c>slots[]</c> array order
/// (which differs from the GameObject child order in the prefab).
/// Each slot is 32×32; the returned Vector2 is the top-left offset in panel coords.
/// Indices 0..13 are valid; out-of-range throws <see cref="ArgumentOutOfRangeException"/>.
/// </summary>
public static class CharacterEquipmentLayout
{
    private static readonly Vector2[] Slots =
    {
        new Vector2(16, 104),  // 0  Weapon
        new Vector2(152, 104), // 1  Shield
        new Vector2(84, 34),   // 2  Helmet
        new Vector2(84, 104),  // 3  Chest
        new Vector2(84, 139),  // 4  Legs
        new Vector2(84, 174),  // 5  Shoes
        new Vector2(50, 69),   // 6  Pauldrons
        new Vector2(50, 139),  // 7  Gloves
        new Vector2(118, 69),  // 8  Cloak
        new Vector2(118, 139), // 9  Belt
        new Vector2(84, 69),   // 10 Necklace
        new Vector2(50, 104),  // 11 Ring1
        new Vector2(118, 104), // 12 Ring2
        new Vector2(152, 174), // 13 Mount
    };

    /// <summary>Returns the top-left offset for the given equipment slot index (0–13).</summary>
    public static Vector2 SlotOffset(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Length)
            throw new System.ArgumentOutOfRangeException(nameof(slotIndex), $"Slot index must be 0–{Slots.Length - 1}.");
        return Slots[slotIndex];
    }
}
