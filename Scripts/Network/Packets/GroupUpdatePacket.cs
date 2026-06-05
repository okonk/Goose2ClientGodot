using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class GroupUpdatePacket : PacketHandler
    {
        public int LineNumber { get; set; }
        public int LoginId { get; set; }
        public string Name { get; set; }
        public string LevelClassName { get; set; }

        public override string Prefix { get; } = "GUD";

        public override object Parse(PacketParser p)
        {
            // "GUD" + index + "," + player.LoginID + "," + player.Name + "," + player.Level + player.Class.ClassName;
            return new GroupUpdatePacket()
            {
                LineNumber = p.GetInt32() - 1,
                LoginId = p.GetInt32(),
                Name = p.GetString(),
                LevelClassName = p.GetString()
            };
        }
    }
}
