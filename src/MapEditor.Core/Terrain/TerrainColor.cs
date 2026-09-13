using System;
using System.Security.Cryptography;
using System.Text;

namespace MapEditor.Core;

public readonly record struct TerrainColor(byte R, byte G, byte B)
{
    public static TerrainColor Derive(Guid terrainId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(terrainId.ToString("N")));
        var seed = (digest[0] << 8) | digest[1];
        var hue = seed * 360.0 / 65536.0;
        var (r, g, b) = HsvToRgb(hue, 0.65, 0.90);
        return new TerrainColor(
            (byte)Math.Round(r * 255.0, MidpointRounding.AwayFromZero),
            (byte)Math.Round(g * 255.0, MidpointRounding.AwayFromZero),
            (byte)Math.Round(b * 255.0, MidpointRounding.AwayFromZero));
    }

    private static (double R, double G, double B) HsvToRgb(double hue, double saturation, double value)
    {
        hue %= 360.0;
        var sector = (int)Math.Floor(hue / 60.0) % 6;
        var fraction = hue / 60.0 - Math.Floor(hue / 60.0);
        var p = value * (1.0 - saturation);
        var q = value * (1.0 - fraction * saturation);
        var t = value * (1.0 - (1.0 - fraction) * saturation);
        return sector switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q)
        };
    }
}
