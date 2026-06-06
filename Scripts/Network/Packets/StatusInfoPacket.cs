using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class StatusInfoPacket : PacketHandler
    {
        public string GuildName { get; set; }
        public string UnknownProperty { get; set; }
        public string ClassName { get; set; }
        public int Level { get; set; }
        public long MaxHP { get; set; }
        public long MaxMP { get; set; }
        public long MaxSP { get; set; }
        public long CurrentHP { get; set; }
        public long CurrentMP { get; set; }
        public long CurrentSP { get; set; }
        public int Strength { get; set; }
        public int Stamina { get; set; }
        public int Intelligence { get; set; }
        public int Dexterity { get; set; }
        public int ArmorClass { get; set; }
        public int FireResist { get; set; }
        public int WaterResist { get; set; }
        public int EarthResist { get; set; }
        public int AirResist { get; set; }
        public int SpiritResist { get; set; }
        public long Gold { get; set; }
        public override string Prefix { get; } = "SNF";

        public override object Parse(PacketParser p)
        {
            // SNFguildname,,classname,level,max_hp,max_mp,max_sp,cur_,cur_mp,cur_sp,stat_str,stat_sta,stat_int,stat_dex,ac,res_f,res_w,res_e,res_a,res_s,gold

            return new StatusInfoPacket()
            {
                GuildName = p.GetString(),
                UnknownProperty = p.GetString(),
                ClassName = p.GetString(),
                Level = p.GetInt32(),
                MaxHP = p.GetInt64(),
                MaxMP = p.GetInt64(),
                MaxSP = p.GetInt64(),
                CurrentHP = p.GetInt64(),
                CurrentMP = p.GetInt64(),
                CurrentSP = p.GetInt64(),
                Strength = p.GetInt32(),
                Stamina = p.GetInt32(),
                Intelligence = p.GetInt32(),
                Dexterity = p.GetInt32(),
                ArmorClass = p.GetInt32(),
                FireResist = p.GetInt32(),
                WaterResist = p.GetInt32(),
                EarthResist = p.GetInt32(),
                AirResist = p.GetInt32(),
                SpiritResist = p.GetInt32(),
                Gold = p.GetInt64()
            };
        }
    }
}
