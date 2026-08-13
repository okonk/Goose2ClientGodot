using Goose2Client.Network.Packets;

namespace Goose2Client
{
    public class ItemStats
    {
        public int SlotNumber { get; set; }
        public int GraphicId { get; set; }
        public int GraphicFile { get; set; }
        public string Title { get; set; }
        public string Name { get; set; }
        public string Surname { get; set; }
        public int StackSize { get; set; }
        public int Value { get; set; }
        public ItemFlags Flags { get; set; }
        public string Description { get; set; }
        public int MinDamage { get; set; }
        public int MaxDamage { get; set; }
        public int Delay { get; set; }
        public ItemMaterial MaterialType { get; set; }
        public int AC { get; set; }
        public int HP { get; set; }
        public int MP { get; set; }
        public int SP { get; set; }
        public int Strength { get; set; }
        public int Stamina { get; set; }
        public int Intelligence { get; set; }
        public int Dexterity { get; set; }
        public int FireResist { get; set; }
        public int WaterResist { get; set; }
        public int EarthResist { get; set; }
        public int AirResist { get; set; }
        public int SpiritResist { get; set; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public int ClassRestrictions1 { get; set; }
        public int ClassRestrictions2 { get; set; }
        public int ClassRestrictions3 { get; set; }
        public int Access { get; set; }
        public int Gender { get; set; }
        public string SpellEffect { get; set; }
        public int SpellEffectChance { get; set; }
        public ItemSlotType SlotType { get; set; }
        public ItemUseType UseType { get; set; }
        public int NotSure { get; set; }
        public int GraphicR { get; set; }
        public int GraphicG { get; set; }
        public int GraphicB { get; set; }
        public int GraphicA { get; set; }

        /// <summary>Currency Value is denominated in. Null on a map object, which carries no
        /// value, and on anything an older server sent.</summary>
        public string CurrencyName { get; set; }

        public static ItemStats FromPacket(InventorySlotPacket packet)
        {
            return new ItemStats
            {
                SlotNumber = packet.SlotNumber,
                GraphicId = packet.GraphicId,
                GraphicFile = packet.GraphicFile,
                Title = packet.Title,
                Name = packet.Name,
                Surname = packet.Surname,
                StackSize = packet.StackSize,
                Value = packet.Value,
                Flags = packet.Flags,
                Description = packet.Description,
                MinDamage = packet.MinDamage,
                MaxDamage = packet.MaxDamage,
                Delay = packet.Delay,
                MaterialType = packet.MaterialType,
                AC = packet.AC,
                HP = packet.HP,
                MP = packet.MP,
                SP = packet.SP,
                Strength = packet.Strength,
                Stamina = packet.Stamina,
                Intelligence = packet.Intelligence,
                Dexterity = packet.Dexterity,
                FireResist = packet.FireResist,
                WaterResist = packet.WaterResist,
                EarthResist = packet.EarthResist,
                AirResist = packet.AirResist,
                SpiritResist = packet.SpiritResist,
                MinLevel = packet.MinLevel,
                MaxLevel = packet.MaxLevel,
                ClassRestrictions1 = packet.ClassRestrictions1,
                ClassRestrictions2 = packet.ClassRestrictions2,
                ClassRestrictions3 = packet.ClassRestrictions3,
                Access = packet.Access,
                Gender = packet.Gender,
                SpellEffect = packet.SpellEffect,
                SpellEffectChance = packet.SpellEffectChance,
                SlotType = packet.SlotType,
                UseType = packet.UseType,
                NotSure = packet.NotSure,
                GraphicR = packet.GraphicR,
                GraphicG = packet.GraphicG,
                GraphicB = packet.GraphicB,
                GraphicA = packet.GraphicA,
                CurrencyName = packet.CurrencyName,
            };
        }

        public static ItemStats FromPacket(MapObjectPacket packet)
        {
            return new ItemStats
            {
                GraphicId = packet.GraphicId,
                GraphicFile = packet.GraphicFile,
                Title = packet.Title,
                Name = packet.Name,
                Surname = packet.Surname,
                StackSize = packet.StackSize,
                MaterialType = packet.MaterialType,
                Flags = packet.Flags,
                GraphicR = packet.GraphicR,
                GraphicG = packet.GraphicG,
                GraphicB = packet.GraphicB,
                GraphicA = packet.GraphicA,
            };
        }
    }
}
