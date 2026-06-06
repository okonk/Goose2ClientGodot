using Goose2Client;
using Goose2Client.Character;
using Xunit;

public class CharacterLayoutTests
{
    [Theory] // base sort = (int)slot + 2 for non-shield/weapon slots, direction-independent
    [InlineData(CharacterSlot.Mount, 2)]
    [InlineData(CharacterSlot.Body, 3)]
    [InlineData(CharacterSlot.Eyes, 4)]
    [InlineData(CharacterSlot.Feet, 5)]
    [InlineData(CharacterSlot.Legs, 6)]
    [InlineData(CharacterSlot.Chest, 7)]
    [InlineData(CharacterSlot.Hair, 8)]
    [InlineData(CharacterSlot.Helm, 9)]
    public void SortOrder_base_is_slot_plus_2(CharacterSlot slot, int expected)
        => Assert.Equal(expected, CharacterLayout.SortOrder(slot, Direction.Down));

    [Theory] // Shield: Right/Up -> 0 (behind), Down/Left -> base 10 (in front)
    [InlineData(Direction.Right, 0)]
    [InlineData(Direction.Up, 0)]
    [InlineData(Direction.Down, 10)]
    [InlineData(Direction.Left, 10)]
    public void SortOrder_shield_is_direction_dependent(Direction d, int expected)
        => Assert.Equal(expected, CharacterLayout.SortOrder(CharacterSlot.Shield, d));

    [Theory] // Weapon: Right/Down -> base 11, Up -> 1, Left -> 0
    [InlineData(Direction.Right, 11)]
    [InlineData(Direction.Down, 11)]
    [InlineData(Direction.Up, 1)]
    [InlineData(Direction.Left, 0)]
    public void SortOrder_weapon_is_direction_dependent(Direction d, int expected)
        => Assert.Equal(expected, CharacterLayout.SortOrder(CharacterSlot.Weapon, d));

    [Theory]
    [InlineData(CharacterSlot.Body, "Bodies")]
    [InlineData(CharacterSlot.Mount, "Bodies")]   // mounts are just bodies
    [InlineData(CharacterSlot.Hair, "Hair")]
    [InlineData(CharacterSlot.Eyes, "Eyes")]
    [InlineData(CharacterSlot.Chest, "Chest")]
    [InlineData(CharacterSlot.Helm, "Helms")]
    [InlineData(CharacterSlot.Legs, "Legs")]
    [InlineData(CharacterSlot.Feet, "Feet")]
    [InlineData(CharacterSlot.Shield, "Hands")]   // shields & weapons share the Hands folder
    [InlineData(CharacterSlot.Weapon, "Hands")]
    public void TypeFolder_matches_converter_output(CharacterSlot slot, string folder)
        => Assert.Equal(folder, CharacterLayout.TypeFolder(slot));

    [Fact]
    public void Underwear_gives_male_default_legs_when_empty()
    {
        Assert.Equal(3, CharacterLayout.UnderwearLegs(bodyId: 1, equippedLegsId: 0));
        Assert.Equal(0, CharacterLayout.UnderwearLegs(bodyId: 1, equippedLegsId: 42)); // keep equipped
    }

    [Fact]
    public void Underwear_gives_female_default_chest_when_empty()
        => Assert.Equal(8, CharacterLayout.UnderwearChest(bodyId: 11, equippedChestId: 0));

    [Fact]
    public void Underwear_falls_back_to_legs_4_for_other_bodies()
        => Assert.Equal(4, CharacterLayout.UnderwearLegs(bodyId: 99, equippedLegsId: 0));
}
