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
    public void InventorySlot_reads_the_trailing_extra_stats()
    {
        var body = InventorySlotBody("gold", "400");

        var p = (InventorySlotPacket)new InventorySlotPacket().Parse(new PacketParser("SIS" + body, "SIS"));

        Assert.Equal("gold", p.CurrencyName);
        Assert.Equal("400", p.ExtraStats);
    }

    [Fact]
    public void InventorySlot_without_extra_stats_parses()
    {
        var body = InventorySlotBody();

        var p = (InventorySlotPacket)new InventorySlotPacket().Parse(new PacketParser("SIS" + body, "SIS"));

        Assert.Null(p.ExtraStats);
        Assert.Null(p.CurrencyName);
        Assert.Equal(0, p.SlotNumber);
        Assert.Equal(0, p.GraphicA);
    }

    [Fact]
    public void VendorSlot_reads_the_trailing_extra_stats()
    {
        var body = InventorySlotBody("credits", "120,30");

        var p = (VendorSlotPacket)new VendorSlotPacket().Parse(new PacketParser("SVS" + body, "SVS"));

        Assert.Equal("credits", p.CurrencyName);
        Assert.Equal("120,30", p.ExtraStats);
    }

    [Fact]
    public void FromPacket_copies_extra_stats()
    {
        var s = ItemStats.FromPacket(new InventorySlotPacket { ExtraStats = "0,800" });

        Assert.Equal(new[] { 0, 800 }, s.ExtraStats);
    }

    [Fact]
    public void FromPacket_ignores_entries_beyond_the_known_stats()
    {
        var entries = Enumerable.Range(0, 16).Select(i => i.ToString());
        var s = ItemStats.FromPacket(new InventorySlotPacket { ExtraStats = string.Join(",", entries) });

        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }, s.ExtraStats);
    }

    [Fact]
    public void FromPacket_treats_malformed_entries_as_zero()
    {
        var s = ItemStats.FromPacket(new InventorySlotPacket { ExtraStats = "400,x" });

        Assert.Equal(new[] { 400, 0 }, s.ExtraStats);
    }

    [Fact]
    public void FromPacket_copies_extra_stats_from_a_vendor_slot_through_the_base_reference()
    {
        var body = InventorySlotBody("credits", "55,70");
        var v = (VendorSlotPacket)new VendorSlotPacket().Parse(new PacketParser("SVS" + body, "SVS"));

        var s = ItemStats.FromPacket((InventorySlotPacket)v);

        Assert.Equal(new[] { 55, 70 }, s.ExtraStats);
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
