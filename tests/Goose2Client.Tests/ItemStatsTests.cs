using Goose2Client;
using Goose2Client.Network.Packets;
using Xunit;

public class ItemStatsTests
{
    [Fact]
    public void FromPacket_copies_core_fields()
    {
        var p = new InventorySlotPacket { SlotNumber = 5, GraphicId = 101, GraphicFile = 7,
            Name = "Sword", StackSize = 3, MaxDamage = 10, UseType = ItemUseType.Weapon };
        var s = ItemStats.FromPacket(p);
        Assert.Equal(5, s.SlotNumber);
        Assert.Equal(101, s.GraphicId);
        Assert.Equal("Sword", s.Name);
        Assert.Equal(3, s.StackSize);
        Assert.Equal(ItemUseType.Weapon, s.UseType);
    }
}
