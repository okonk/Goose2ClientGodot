using MapEditor.GameData.Rows;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class NpcEquipmentParserTests
{
    [Fact]
    public void TryParse_DefaultStringYieldsSixEmptyUntintedSlots()
    {
        Assert.True(NpcEquipmentParser.TryParse(NpcEquipmentParser.DefaultEquippedItems, out NpcEquipment equipment, out string? diagnostic));

        Assert.Null(diagnostic);
        RgbaValue none = new(0, 0, 0, 0);
        Assert.Equal(new NpcEquipment(
            new NpcEquipmentSlot(0, none),
            new NpcEquipmentSlot(0, none),
            new NpcEquipmentSlot(0, none),
            new NpcEquipmentSlot(0, none),
            new NpcEquipmentSlot(0, none),
            new NpcEquipmentSlot(0, none)), equipment);
    }

    [Fact]
    public void TryParse_SixUntintedSlotsParseInChestHeadLegsFeetShieldWeaponOrder()
    {
        Assert.True(NpcEquipmentParser.TryParse("1,*,2,*,3,*,4,*,5,*,6,*", out NpcEquipment equipment, out string? diagnostic));

        Assert.Null(diagnostic);
        Assert.Equal(1, equipment.Chest.Id);
        Assert.Equal(2, equipment.Helm.Id);
        Assert.Equal(3, equipment.Legs.Id);
        Assert.Equal(4, equipment.Feet.Id);
        Assert.Equal(5, equipment.Shield.Id);
        Assert.Equal(6, equipment.Weapon.Id);
    }

    [Fact]
    public void TryParse_MixedTintedSlotsRetainExactBytes()
    {
        Assert.True(NpcEquipmentParser.TryParse("1,10,20,30,40,2,*,3,0,0,0,0,4,*,5,255,254,253,252,6,*", out NpcEquipment equipment, out string? diagnostic));

        Assert.Null(diagnostic);
        Assert.Equal(new RgbaValue(10, 20, 30, 40), equipment.Chest.Tint);
        Assert.Equal(new RgbaValue(0, 0, 0, 0), equipment.Helm.Tint);
        Assert.Equal(new RgbaValue(0, 0, 0, 0), equipment.Legs.Tint);
        Assert.Equal(new RgbaValue(255, 254, 253, 252), equipment.Shield.Tint);
        Assert.Equal(new RgbaValue(0, 0, 0, 0), equipment.Weapon.Tint);
    }

    [Theory]
    [InlineData(" 0,*,0,*,0,*,0,*,0,*,0,*")]
    [InlineData("0, *,0,*,0,*,0,*,0,*,0,*")]
    [InlineData("0,*,0,*,0,*,0,*,0,*,0,* ")]
    [InlineData("")]
    [InlineData("0,*,0,*,0,*,0,*,0,*")]
    [InlineData("0,*,0,*,0,*,0,*,0,*,0,*,7,8,9,10")]
    [InlineData("0,*,0,*,0,*,0,*,0,*,0,*,1")]
    [InlineData("2147483648,*,0,*,0,*,0,*,0,*,0,*")]
    [InlineData("-1,*,0,*,0,*,0,*,0,*,0,*")]
    [InlineData("0,,0,*,0,*,0,*,0,*,0,*")]
    [InlineData("0,*,0,*,0,*,0,*,0,*,0,256")]
    [InlineData("0,*,0,*,0,*,0,*,0,*,0,-1")]
    [InlineData("0,*,0,*,0,*,0,*,0,*,0,abc")]
    [InlineData("0,*,0,*,0,*,0,*,0,*,0,*,*")]
    public void TryParse_MalformedStringYieldsOneDiagnosticAndNoEquipment(string input)
    {
        Assert.False(NpcEquipmentParser.TryParse(input, out NpcEquipment equipment, out string diagnostic));

        Assert.Equal(default, equipment);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
    }

    [Fact]
    public void TryParse_NullInputYieldsOneDiagnostic()
    {
        Assert.False(NpcEquipmentParser.TryParse(null, out NpcEquipment equipment, out string diagnostic));

        Assert.Equal(default, equipment);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
    }
}
