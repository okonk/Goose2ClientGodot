using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Gif;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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

        using var expected = Image.Load<Rgba32>(Paths.UnityPng(1000));
        Assert.Equal(w, expected.Width);
        Assert.Equal(h, expected.Height);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Rgba32 e = expected[x, y];
                int i = (y * w + x) * 4;
                byte ar = rgba[i], ag = rgba[i + 1], ab = rgba[i + 2], aa = rgba[i + 3];

                // Both fully transparent → equal regardless of RGB (Unity's
                // alphaIsTransparency may bleed color under transparent pixels).
                if (e.A == 0 && aa == 0) continue;

                if (e.A != aa || e.R != ar || e.G != ag || e.B != ab)
                    Assert.Fail(
                        $"Pixel ({x},{y}) expected RGBA({e.R},{e.G},{e.B},{e.A}) " +
                        $"got RGBA({ar},{ag},{ab},{aa})");
            }
        }
    }
}
