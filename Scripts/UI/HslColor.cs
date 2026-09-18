using System;

namespace Goose2Client.UI;

public static class HslColor
{
    // h in degrees (any range, wrapped to [0,360)), s/l in [0,1] (clamped) -> 0..255
    public static (int R, int G, int B) ToRgb(float h, float s, float l)
    {
        h = ((h % 360f) + 360f) % 360f;
        s = Math.Clamp(s, 0f, 1f);
        l = Math.Clamp(l, 0f, 1f);
        float c = (1f - Math.Abs(2f * l - 1f)) * s;
        float hp = h / 60f;
        float x = c * (1f - Math.Abs(hp % 2f - 1f));
        (float r1, float g1, float b1) = hp switch
        {
            < 1 => (c, x, 0f),
            < 2 => (x, c, 0f),
            < 3 => (0f, c, x),
            < 4 => (0f, x, c),
            < 5 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        float m = l - c / 2f;
        return (To255(r1 + m), To255(g1 + m), To255(b1 + m));
    }

    // 0..255 -> h in [0,360), s/l in [0,1]. Grey (max==min) returns h=0, s=0.
    public static (float H, float S, float L) FromRgb(int r, int g, int b)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float l = (max + min) / 2f;
        if (max == min) return (0f, 0f, l);
        float d = max - min;
        float s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
        float h = max == rf
            ? (gf - bf) / d + (gf < bf ? 6f : 0f)
            : max == gf
                ? (bf - rf) / d + 2f
                : (rf - gf) / d + 4f;
        return (h * 60f, s, l);
    }

    private static int To255(float v) => (int)MathF.Round(v * 255f, MidpointRounding.AwayFromZero);
}
