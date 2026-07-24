using Goose2.AssetConverter.Png;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace AssetConverter.Tests;

public class PayloadDecoderTests
{
    private static byte[] BmpBytes(params Rgba32[] pixels)
    {
        using var img = new Image<Rgba32>(pixels.Length, 1);
        for (int i = 0; i < pixels.Length; i++) img[i, 0] = pixels[i];
        using var ms = new MemoryStream();
        img.SaveAsBmp(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Bmp_BlackAndNearBlackBecomeTransparent()
    {
        // black, near-black (1,0,0), and an opaque colour
        var payload = BmpBytes(new Rgba32(0, 0, 0), new Rgba32(1, 0, 0), new Rgba32(200, 50, 50));

        var rgba = PayloadDecoder.ToRgba(payload, out int w, out int h);

        Assert.Equal((3, 1), (w, h));
        Assert.Equal(0, rgba[3]);        // black -> alpha 0
        Assert.Equal(0, rgba[7]);        // (1,0,0) -> alpha 0 (matches GifLoader.cs:100 rule)
        Assert.Equal(255, rgba[11]);     // colour stays opaque
        Assert.Equal(200, rgba[8]);      // rgb preserved
    }

    [Fact]
    public void Gif_DelegatesToGifLoader()
    {
        // Real Illutia GIF payload: sheet 1000 (see AdfFileTests.DecodedFileData_LooksLikeAGif)
        var adf = new Goose2.AssetConverter.Adf.AdfFile(Goose2.AssetConverter.Paths.Adf(1000));
        var expected = Goose2.AssetConverter.Gif.GifLoader.Load(adf.FileData, out int ew, out int eh);

        var actual = PayloadDecoder.ToRgba(adf.FileData, out int aw, out int ah);

        Assert.Equal((ew, eh), (aw, ah));
        Assert.Equal(expected, actual);
    }
}
