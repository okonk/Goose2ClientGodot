using System.Linq;
using Goose2Client;
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

public class ItemStatsTests
{
    /// <summary>An SIS body: 43 fields, slot number first, everything else zeroed. The
    /// currency name the server appends after GraphicA goes in <paramref name="trailing"/>.</summary>
    private static string InventorySlotBody(params string[] trailing)
    {
        var fields = Enumerable.Repeat("0", 43).ToArray();
        fields[0] = "1";
        return string.Join("|", fields.Concat(trailing));
    }

    [Fact]
    public void InventorySlot_reads_the_trailing_currency_name()
    {
        var body = InventorySlotBody("spirit");

        var p = (InventorySlotPacket)new InventorySlotPacket().Parse(new PacketParser("SIS" + body, "SIS"));

        Assert.Equal("spirit", p.CurrencyName);
        Assert.Equal(0, p.GraphicA);
    }

    /// <summary>A server that predates the field must still parse, with every earlier field
    /// landing where it always did.</summary>
    [Fact]
    public void InventorySlot_without_a_currency_name_still_parses()
    {
        var body = InventorySlotBody();

        var p = (InventorySlotPacket)new InventorySlotPacket().Parse(new PacketParser("SIS" + body, "SIS"));

        Assert.Null(p.CurrencyName);
        Assert.Equal(0, p.SlotNumber);
        Assert.Equal(0, p.GraphicA);
    }

    [Fact]
    public void VendorSlot_reads_the_trailing_currency_name()
    {
        var body = InventorySlotBody("credits");

        var p = (VendorSlotPacket)new VendorSlotPacket().Parse(new PacketParser("SVS" + body, "SVS"));

        Assert.Equal("credits", p.CurrencyName);
    }

    [Fact]
    public void FromPacket_copies_the_currency_name()
    {
        var s = ItemStats.FromPacket(new InventorySlotPacket { CurrencyName = "spirit" });

        Assert.Equal("spirit", s.CurrencyName);
    }

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
