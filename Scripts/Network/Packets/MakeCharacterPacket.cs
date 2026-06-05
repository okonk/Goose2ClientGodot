using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class MakeCharacterPacket : PacketHandler
    {
        public int LoginId { get; set; }
        public CharacterType CharacterType { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string Surname { get; set; }
        public string GuildName { get; set; }
        public int MapX { get; set; }
        public int MapY { get; set; }
        public Direction Facing { get; set; }
        public float HPPercent { get; set; }
        public int BodyId { get; set; }
        public int BodyR { get; set; }
        public int BodyG { get; set; }
        public int BodyB { get; set; }
        public int BodyA { get; set; }
        public int BodyState { get; set; }
        public int HairId { get; set; }
        public int[][] DisplayedEquipment { get; set; }
        public int HairR { get; set; }
        public int HairG { get; set; }
        public int HairB { get; set; }
        public int HairA { get; set; }
        public int Invisible { get; set; }
        public int FaceId { get; set; }
        public int MoveSpeed { get; set; }
        public bool IsGM { get; set; }

        public override string Prefix { get; } = "MKC";

        public override object Parse(PacketParser p)
        {
            var packet = new MakeCharacterPacket()
            {
                LoginId = p.GetInt32(),
                CharacterType = (CharacterType)p.GetInt32(),
                Name = p.GetString(),
                Title = p.GetString(),
                Surname = p.GetString(),
                GuildName = p.GetString(),
                MapX = p.GetInt32() - 1,
                MapY = p.GetInt32() - 1,
                Facing = (Direction)(p.GetInt32() - 1),
                HPPercent = p.GetInt32() / 100.0f,
                BodyId = p.GetInt32(),
                BodyR = p.GetInt32(),
                BodyG = p.GetInt32(),
                BodyB = p.GetInt32(),
                BodyA = p.GetInt32(),
                BodyState = p.GetInt32(),
            };

            if (packet.BodyId < 100) {
                // parse as normal
                packet.HairId = p.GetInt32();
                packet.DisplayedEquipment = ParseEquippedItems(p);
                packet.HairR = p.GetInt32();
                packet.HairG = p.GetInt32();
                packet.HairB = p.GetInt32();
                packet.HairA = p.GetInt32();
                packet.Invisible = p.GetInt32();
                packet.FaceId = p.GetInt32();
                packet.MoveSpeed = p.GetInt32();
                packet.IsGM = p.GetBool();

                // mount
                ParseItem(packet.DisplayedEquipment, 6, p);

                // Fixes bug in the server sending the wrong state
                if (packet.DisplayedEquipment[4][0] == 0 && packet.DisplayedEquipment[5][0] == 0)
                    packet.BodyState = 3;
            }
            else {
                // monster so skip stuff..
                // hairid
                // equipment doesn't exist
                // hair r
                // hair g
                // hair b
                // hair a
                p.GetString(); // invisible
                // face id
                packet.MoveSpeed = p.GetInt32();
                packet.IsGM = p.GetBool();
                // mount stuff
            }

            return packet;
        }

        public int[][] ParseEquippedItems(PacketParser p)
        {
            // Chest, Head, Legs, Feet, Shield, Weapon, Mount
            var equipped = new int[7][];
            for (int i = 0; i < 6; i++)
            {
                ParseItem(equipped, i, p);
            }

            return equipped;
        }

        private void ParseItem(int[][] equipped, int i, PacketParser p)
        {
            equipped[i] = new int[5];
            int j = 0;
            int id = p.GetInt32();   // item graphic id
            equipped[i][j++] = id;

            if (id == 0 && i == 6) return; // hack for mounts for npc characters, server used to send only 4 ints instead of 5

            if (p.Peek() == '*')
            {
                p.GetString(); // eat the string
                equipped[i][j++] = 0; // r
                equipped[i][j++] = 0; // g
                equipped[i][j++] = 0; // b
                equipped[i][j++] = 0; // a
            }
            else
            {
                equipped[i][j++] = p.GetInt32(); // r
                equipped[i][j++] = p.GetInt32(); // g
                equipped[i][j++] = p.GetInt32(); // b
                equipped[i][j++] = p.GetInt32(); // a
            }
        }
    }
}
