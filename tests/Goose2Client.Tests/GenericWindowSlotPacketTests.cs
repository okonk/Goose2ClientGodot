using System.Linq;
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests
{
    public class GenericWindowSlotPacketTests
    {
        private static string SlotBody(params string[] trailing)
        {
            var fields = Enumerable.Repeat("0", 43).ToArray();
            fields[0] = "3";
            fields[1] = "7";
            fields[4] = "Sword";
            return string.Join("|", fields.Concat(trailing));
        }

        private static void AssertSlotFieldsMatch(InventorySlotPacket a, InventorySlotPacket b)
        {
            Assert.Equal(a.SlotNumber, b.SlotNumber);
            Assert.Equal(a.GraphicId, b.GraphicId);
            Assert.Equal(a.GraphicFile, b.GraphicFile);
            Assert.Equal(a.Title, b.Title);
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.Surname, b.Surname);
            Assert.Equal(a.StackSize, b.StackSize);
            Assert.Equal(a.Value, b.Value);
            Assert.Equal(a.Flags, b.Flags);
            Assert.Equal(a.Description, b.Description);
            Assert.Equal(a.MinDamage, b.MinDamage);
            Assert.Equal(a.MaxDamage, b.MaxDamage);
            Assert.Equal(a.Delay, b.Delay);
            Assert.Equal(a.MaterialType, b.MaterialType);
            Assert.Equal(a.AC, b.AC);
            Assert.Equal(a.HP, b.HP);
            Assert.Equal(a.MP, b.MP);
            Assert.Equal(a.SP, b.SP);
            Assert.Equal(a.Strength, b.Strength);
            Assert.Equal(a.Stamina, b.Stamina);
            Assert.Equal(a.Intelligence, b.Intelligence);
            Assert.Equal(a.Dexterity, b.Dexterity);
            Assert.Equal(a.FireResist, b.FireResist);
            Assert.Equal(a.WaterResist, b.WaterResist);
            Assert.Equal(a.EarthResist, b.EarthResist);
            Assert.Equal(a.AirResist, b.AirResist);
            Assert.Equal(a.SpiritResist, b.SpiritResist);
            Assert.Equal(a.MinLevel, b.MinLevel);
            Assert.Equal(a.MaxLevel, b.MaxLevel);
            Assert.Equal(a.ClassRestrictions1, b.ClassRestrictions1);
            Assert.Equal(a.ClassRestrictions2, b.ClassRestrictions2);
            Assert.Equal(a.ClassRestrictions3, b.ClassRestrictions3);
            Assert.Equal(a.Access, b.Access);
            Assert.Equal(a.Gender, b.Gender);
            Assert.Equal(a.SpellEffect, b.SpellEffect);
            Assert.Equal(a.SpellEffectChance, b.SpellEffectChance);
            Assert.Equal(a.SlotType, b.SlotType);
            Assert.Equal(a.UseType, b.UseType);
            Assert.Equal(a.NotSure, b.NotSure);
            Assert.Equal(a.GraphicR, b.GraphicR);
            Assert.Equal(a.GraphicG, b.GraphicG);
            Assert.Equal(a.GraphicB, b.GraphicB);
            Assert.Equal(a.GraphicA, b.GraphicA);
            Assert.Equal(a.CurrencyName, b.CurrencyName);
            Assert.Equal(a.ExtraStats, b.ExtraStats);
            Assert.Equal(a.MinExperience, b.MinExperience);
        }

        [Fact]
        public void GWS_ParsesWindowIdThenSlotFields()
        {
            var p = (GenericWindowSlotPacket)new GenericWindowSlotPacket().Parse(new PacketParser("GWS1042|" + SlotBody(), "GWS"));

            Assert.Equal(1042, p.WindowId);
            Assert.Equal(2, p.SlotNumber);
            Assert.Equal(7, p.GraphicId);
            Assert.Equal(0, p.GraphicFile);
            Assert.Equal("Sword", p.Name);
        }

        [Fact]
        public void GWS_TailFieldsMatchSIS()
        {
            var body = SlotBody("gold", "400", "20000000");

            var gws = (GenericWindowSlotPacket)new GenericWindowSlotPacket().Parse(new PacketParser("GWS1042|" + body, "GWS"));
            var sis = (InventorySlotPacket)new InventorySlotPacket().Parse(new PacketParser("SIS" + body, "SIS"));

            AssertSlotFieldsMatch(sis, gws);
        }

        [Fact]
        public void GWC_ParsesWindowIdAndSlot()
        {
            var p = (ClearGenericWindowSlotPacket)new ClearGenericWindowSlotPacket().Parse(new PacketParser("GWC1042,4", "GWC"));

            Assert.Equal(1042, p.WindowId);
            Assert.Equal(3, p.SlotNumber);
        }

        [Fact]
        public void SBS_Regression()
        {
            var p = (BankSlotPacket)new BankSlotPacket().Parse(new PacketParser("SBS" + SlotBody("gold", "400", "20000000"), "SBS"));

            Assert.Equal(2, p.SlotNumber);
            Assert.Equal(7, p.GraphicId);
            Assert.Equal("Sword", p.Name);
            Assert.Equal("gold", p.CurrencyName);
            Assert.Equal("400", p.ExtraStats);
            Assert.Equal(20_000_000, p.MinExperience);
        }
    }
}
