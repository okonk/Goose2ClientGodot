using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class ZoneAddPacket : PacketHandler
    {
        public int Id { get; set; }

        public int AnimationId { get; set; }

        public long RemainingMs { get; set; }

        public List<(int X, int Y)> Tiles { get; set; } = new();

        public override string Prefix { get; } = "ZADD";

        // ZADD id,anim,file,remainingMs,x1,y1,… — tiles are 1-based on the wire; the file is unused, as with SPA.
        public override object Parse(PacketParser p)
        {
            var packet = new ZoneAddPacket
            {
                Id = p.GetInt32(),
                AnimationId = p.GetInt32(),
            };
            p.GetInt32();
            packet.RemainingMs = p.GetInt64();

            while (p.TokensRemaining() >= 2)
                packet.Tiles.Add((p.GetInt32() - 1, p.GetInt32() - 1));

            return packet;
        }
    }
}
