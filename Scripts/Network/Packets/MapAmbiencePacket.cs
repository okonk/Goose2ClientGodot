using System;
using Goose2Client;

namespace Goose2Client.Network.Packets
{
    // Wire format: AMB<startAlpha‰>,<endAlpha‰>,<tint>,<fill>,<elapsedMs>,<durationMs>,<weather> — colors 0xRRGGBBAA as decimal (server: P.MapAmbience).
    public class MapAmbiencePacket : PacketHandler
    {
        public float StartAlpha { get; set; }
        public float EndAlpha { get; set; }
        public uint TintColor { get; set; }
        public uint FillColor { get; set; }
        public long ElapsedMs { get; set; }
        public long DurationMs { get; set; }
        public WeatherType Weather { get; set; }

        public override string Prefix { get; } = "AMB";

        public override object Parse(PacketParser p)
        {
            var startAlpha = p.GetInt32();
            var endAlpha = p.GetInt32();
            var tint = (uint)p.GetInt64();
            var fill = (uint)p.GetInt64();
            var elapsedMs = p.GetInt64();
            var durationMs = p.GetInt64();
            var weather = (byte)p.GetInt32();

            return new MapAmbiencePacket()
            {
                StartAlpha = startAlpha / 1000f,
                EndAlpha = endAlpha / 1000f,
                TintColor = tint,
                FillColor = fill,
                ElapsedMs = elapsedMs,
                DurationMs = durationMs,
                Weather = Enum.IsDefined(typeof(WeatherType), weather) ? (WeatherType)weather : WeatherType.Clear
            };
        }
    }
}
