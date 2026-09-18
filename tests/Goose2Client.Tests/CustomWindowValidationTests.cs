using Goose2Client.Character;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class CustomWindowValidationTests
{
    private static ItemStats Item(ItemUseType useType, ItemSlotType slotType)
    {
        return new ItemStats { UseType = useType, SlotType = slotType };
    }

    [Fact]
    public void IsValidCandidate_armorChestpiece_true()
    {
        Assert.True(CustomWindowValidation.IsValidCandidate(Item(ItemUseType.Armor, ItemSlotType.Chestpiece)));
    }

    [Fact]
    public void IsValidCandidate_weapon_true()
    {
        Assert.True(CustomWindowValidation.IsValidCandidate(Item(ItemUseType.Weapon, ItemSlotType.Weapon)));
    }

    [Theory]
    [InlineData(ItemSlotType.Pauldrons)]
    [InlineData(ItemSlotType.Gloves)]
    [InlineData(ItemSlotType.Cloak)]
    [InlineData(ItemSlotType.Belt)]
    [InlineData(ItemSlotType.Necklace)]
    [InlineData(ItemSlotType.Bracelet)]
    public void IsValidCandidate_excludedSlotType_false(ItemSlotType slotType)
    {
        Assert.False(CustomWindowValidation.IsValidCandidate(Item(ItemUseType.Armor, slotType)));
    }

    [Theory]
    [InlineData(ItemUseType.NoUse)]
    [InlineData(ItemUseType.OneTime)]
    public void IsValidCandidate_nonEquipUseType_false(ItemUseType useType)
    {
        Assert.False(CustomWindowValidation.IsValidCandidate(Item(useType, ItemSlotType.Chestpiece)));
    }

    [Fact]
    public void TypesCompatible_sameType_true()
    {
        Assert.True(CustomWindowValidation.TypesCompatible(Item(ItemUseType.Armor, ItemSlotType.Chestpiece), Item(ItemUseType.Armor, ItemSlotType.Chestpiece)));
    }

    [Fact]
    public void TypesCompatible_differentArmorType_false()
    {
        Assert.False(CustomWindowValidation.TypesCompatible(Item(ItemUseType.Armor, ItemSlotType.Chestpiece), Item(ItemUseType.Armor, ItemSlotType.Helmet)));
    }

    [Fact]
    public void TypesCompatible_weapons_true()
    {
        // 1H and 2H weapons both wire as ItemSlotType.Weapon (11)
        Assert.True(CustomWindowValidation.TypesCompatible(Item(ItemUseType.Weapon, ItemSlotType.Weapon), Item(ItemUseType.Weapon, ItemSlotType.Weapon)));
    }

    [Fact]
    public void TypesCompatible_mounts_true()
    {
        Assert.True(CustomWindowValidation.TypesCompatible(Item(ItemUseType.Armor, ItemSlotType.Mount), Item(ItemUseType.Armor, ItemSlotType.Mount)));
    }

    private sealed class FrameWindow : IWindow
    {
        public FrameWindow(WindowFrames frame) => WindowFrame = frame;
        public WindowFrames WindowFrame { get; }
        public int WindowId => 0;
    }

    [Fact]
    public void IsInventorySource_inventory_true()
    {
        Assert.True(CustomWindowValidation.IsInventorySource(new FrameWindow(WindowFrames.Inventory)));
    }

    [Theory]
    [InlineData(WindowFrames.Equipped)]
    [InlineData(WindowFrames.TwoSlot)]
    [InlineData(WindowFrames.Vendor)]
    [InlineData(WindowFrames.Custom)]
    public void IsInventorySource_otherFrames_false(WindowFrames frame)
    {
        Assert.False(CustomWindowValidation.IsInventorySource(new FrameWindow(frame)));
    }

    [Theory]
    [InlineData(ItemSlotType.Helmet, CharacterSlot.Helm)]
    [InlineData(ItemSlotType.Chestpiece, CharacterSlot.Chest)]
    [InlineData(ItemSlotType.Pants, CharacterSlot.Legs)]
    [InlineData(ItemSlotType.Shoes, CharacterSlot.Feet)]
    [InlineData(ItemSlotType.Weapon, CharacterSlot.Weapon)]
    [InlineData(ItemSlotType.Shield, CharacterSlot.Shield)]
    public void PreviewTarget_mappings(ItemSlotType slotType, CharacterSlot expected)
    {
        Assert.Equal(expected, CustomWindowValidation.PreviewTarget(Item(ItemUseType.Armor, slotType)));
    }

    [Fact]
    public void PreviewTarget_mount_null()
    {
        Assert.Null(CustomWindowValidation.PreviewTarget(Item(ItemUseType.Armor, ItemSlotType.Mount)));
    }

    [Theory]
    [InlineData(ItemSlotType.Pauldrons)]
    [InlineData(ItemSlotType.Gloves)]
    [InlineData(ItemSlotType.Cloak)]
    [InlineData(ItemSlotType.Belt)]
    [InlineData(ItemSlotType.Necklace)]
    [InlineData(ItemSlotType.Bracelet)]
    public void PreviewTarget_excludedSlotType_null(ItemSlotType slotType)
    {
        Assert.Null(CustomWindowValidation.PreviewTarget(Item(ItemUseType.Armor, slotType)));
    }
}
