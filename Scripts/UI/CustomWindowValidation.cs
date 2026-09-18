using Goose2Client;
using Goose2Client.Character;

namespace Goose2Client.UI;

public static class CustomWindowValidation
{
    public static bool IsValidCandidate(ItemStats item)
    {
        if (item == null)
        {
            return false;
        }

        if (item.UseType != ItemUseType.Armor && item.UseType != ItemUseType.Weapon)
        {
            return false;
        }

        return item.SlotType switch
        {
            ItemSlotType.Pauldrons or ItemSlotType.Gloves or ItemSlotType.Cloak or
            ItemSlotType.Belt or ItemSlotType.Necklace or ItemSlotType.Bracelet => false,
            _ => true,
        };
    }

    public static bool TypesCompatible(ItemStats a, ItemStats b)
    {
        return a != null && b != null && a.SlotType == b.SlotType;
    }

    public static bool IsInventorySource(IWindow srcWindow)
    {
        return srcWindow != null && srcWindow.WindowFrame == WindowFrames.Inventory;
    }

    public static CharacterSlot? PreviewTarget(ItemStats item)
    {
        if (item == null)
        {
            return null;
        }

        // Mounts pass validation but have no preview target by design.
        return item.SlotType switch
        {
            ItemSlotType.Helmet => CharacterSlot.Helm,
            ItemSlotType.Chestpiece => CharacterSlot.Chest,
            ItemSlotType.Pants => CharacterSlot.Legs,
            ItemSlotType.Shoes => CharacterSlot.Feet,
            ItemSlotType.Weapon => CharacterSlot.Weapon,
            ItemSlotType.Shield => CharacterSlot.Shield,
            _ => null,
        };
    }
}
