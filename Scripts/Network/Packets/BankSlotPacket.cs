using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class BankSlotPacket : InventorySlotPacket
    {
        public override string Prefix { get; } = "SBS";

        public override object Parse(PacketParser p)
        {
            p.Delimeter = '|';

            return new BankSlotPacket()
            {
                SlotNumber = p.GetInt32() - 1,
                GraphicId = p.GetInt32(),
                GraphicFile = p.GetInt32(),
                Title = p.GetString(),
                Name = p.GetString(),
                Surname = p.GetString(),
                StackSize = p.GetInt32(),
                Value = p.GetInt32(),
                Flags = (ItemFlags)p.GetInt32(),
                Description = p.GetString(),
                MinDamage = p.GetInt32(),
                MaxDamage = p.GetInt32(),
                Delay = p.GetInt32(),
                MaterialType = (ItemMaterial)p.GetInt32(),
                AC = p.GetInt32(),
                HP = p.GetInt32(),
                MP = p.GetInt32(),
                SP = p.GetInt32(),
                Strength = p.GetInt32(),
                Stamina = p.GetInt32(),
                Intelligence = p.GetInt32(),
                Dexterity = p.GetInt32(),
                FireResist = p.GetInt32(),
                WaterResist = p.GetInt32(),
                EarthResist = p.GetInt32(),
                AirResist = p.GetInt32(),
                SpiritResist = p.GetInt32(),
                MinLevel = p.GetInt32(),
                MaxLevel = p.GetInt32(),
                ClassRestrictions1 = p.GetInt32(),
                ClassRestrictions2 = p.GetInt32(),
                ClassRestrictions3 = p.GetInt32(),
                Access = p.GetInt32(),
                Gender = p.GetInt32(),
                SpellEffect = p.GetString(),
                SpellEffectChance = p.GetInt32(),
                SlotType = (ItemSlotType)p.GetInt32(),
                UseType = (ItemUseType)p.GetInt32(),
                NotSure = p.GetInt32(),
                GraphicR = p.GetInt32(),
                GraphicG = p.GetInt32(),
                GraphicB = p.GetInt32(),
                GraphicA = p.GetInt32(),
            };
        }
    }
}
