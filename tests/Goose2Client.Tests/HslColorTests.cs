using System;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class HslColorTests
{
    [Theory]
    [InlineData(0f, 255, 0, 0)]
    [InlineData(120f, 0, 255, 0)]
    [InlineData(240f, 0, 0, 255)]
    [InlineData(60f, 255, 255, 0)]
    [InlineData(180f, 0, 255, 255)]
    [InlineData(300f, 255, 0, 255)]
    public void ToRgb_PrimitivesAtFullSaturationMidLightness(float h, int r, int g, int b)
    {
        Assert.Equal((r, g, b), HslColor.ToRgb(h, 1f, 0.5f));
    }

    [Fact]
    public void ToRgb_ZeroSaturationIsGrey()
    {
        Assert.Equal((255, 255, 255), HslColor.ToRgb(0f, 0f, 1f));
        Assert.Equal((0, 0, 0), HslColor.ToRgb(0f, 0f, 0f));
        Assert.Equal((128, 128, 128), HslColor.ToRgb(0f, 0f, 0.5f));
    }

    [Fact]
    public void ToRgb_ClampsInputs()
    {
        Assert.Equal(HslColor.ToRgb(40f, 1f, 0f), HslColor.ToRgb(400f, 2f, -1f));
        Assert.Equal(HslColor.ToRgb(330f, 1f, 0.5f), HslColor.ToRgb(-30f, 1f, 0.5f));
    }

    [Theory]
    [InlineData(255, 0, 0, 0f, 1f, 0.5f)]
    [InlineData(0, 255, 0, 120f, 1f, 0.5f)]
    [InlineData(255, 255, 255, 0f, 0f, 1f)]
    [InlineData(0, 0, 0, 0f, 0f, 0f)]
    public void FromRgb_KnownValues(int r, int g, int b, float h, float s, float l)
    {
        var (fh, fs, fl) = HslColor.FromRgb(r, g, b);
        Assert.Equal(h, fh, 2);
        Assert.Equal(s, fs, 3);
        Assert.Equal(l, fl, 3);
    }

    [Fact]
    public void FromRgb_MidGrey()
    {
        var (h, s, l) = HslColor.FromRgb(128, 128, 128);
        Assert.Equal(0f, h);
        Assert.Equal(0f, s);
        Assert.InRange(l, 0.49f, 0.51f);
    }

    [Theory]
    [InlineData(10f, 0.8f, 0.3f)]
    [InlineData(100f, 0.6f, 0.5f)]
    [InlineData(200f, 1f, 0.7f)]
    [InlineData(300f, 0.55f, 0.2f)]
    public void RoundTrip(float h, float s, float l)
    {
        var (r, g, b) = HslColor.ToRgb(h, s, l);
        var (fh, fs, fl) = HslColor.FromRgb(r, g, b);
        float dh = Math.Abs(fh - h);
        dh = Math.Min(dh, 360f - dh);
        Assert.True(dh <= 2f, $"hue {fh} vs {h}");
        Assert.InRange(fs, s - 0.01f, s + 0.01f);
        Assert.InRange(fl, l - 0.01f, l + 0.01f);
    }

    [Fact]
    public void RoundTrip_SweepsAllHueSectors()
    {
        for (int h = 0; h < 360; h += 10)
        {
            var (r, g, b) = HslColor.ToRgb(h, 1f, 0.5f);
            var (fh, fs, fl) = HslColor.FromRgb(r, g, b);
            float dh = Math.Abs(fh - h);
            dh = Math.Min(dh, 360f - dh);
            Assert.True(dh <= 2f, $"hue {fh} vs {h}");
            Assert.InRange(fs, 0.99f, 1.01f);
            Assert.InRange(fl, 0.49f, 0.51f);
        }
    }
}
