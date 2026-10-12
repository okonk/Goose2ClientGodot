using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Gif;
using SkiaSharp;
using Xunit;

namespace AssetConverter.Tests;

[Collection("AdfFileCollection")]
public class GoldenImageTests
{
    [Fact]
    public void Sheet1000_PixelMatchesUnityPng()
    {
        var adf = new AdfFile(Paths.Adf(1000));
        var rgba = GifLoader.Load(adf.FileData, out int w, out int h);

        using SKBitmap expected = SKBitmap.Decode(Paths.UnityPng(1000))!;
        Assert.Equal(w, expected.Width);
        Assert.Equal(h, expected.Height);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                SKColor e = expected.GetPixel(x, y);
                int i = (y * w + x) * 4;
                byte ar = rgba[i], ag = rgba[i + 1], ab = rgba[i + 2], aa = rgba[i + 3];

                // Both fully transparent → equal regardless of RGB (Unity's
                // alphaIsTransparency may bleed color under transparent pixels).
                if (e.Alpha == 0 && aa == 0) continue;

                if (e.Alpha != aa || e.Red != ar || e.Green != ag || e.Blue != ab)
                    Assert.Fail(
                        $"Pixel ({x},{y}) expected RGBA({e.Red},{e.Green},{e.Blue},{e.Alpha}) " +
                        $"got RGBA({ar},{ag},{ab},{aa})");
            }
        }
    }
}
