using Goose2.AssetConverter.Png;
using SkiaSharp;
using Xunit;

namespace AssetConverter.Tests;

public class PayloadDecoderTests
{
    private static byte[] BmpBytes(int width, params SKColor[] pixels)
    {
        int height = pixels.Length / width;
        int rowLength = (width * 3 + 3) & ~3;
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(54 + rowLength * height);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(0);
        writer.Write(rowLength * height);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                SKColor pixel = pixels[y * width + x];
                writer.Write(pixel.Blue);
                writer.Write(pixel.Green);
                writer.Write(pixel.Red);
            }

            writer.Write(new byte[rowLength - width * 3]);
        }

        writer.Flush();
        return stream.ToArray();
    }

    [Fact]
    public void Bmp_BlackAndNearBlackBecomeTransparent()
    {
        // black, near-black (1,0,0), and an opaque colour
        var payload = BmpBytes(3, new SKColor(0, 0, 0), new SKColor(1, 0, 0), new SKColor(200, 50, 50));

        var rgba = PayloadDecoder.ToRgba(payload, out int w, out int h);

        Assert.Equal((3, 1), (w, h));
        Assert.Equal(0, rgba[3]);        // black -> alpha 0
        Assert.Equal(0, rgba[7]);        // (1,0,0) -> alpha 0 (matches GifLoader.cs:100 rule)
        Assert.Equal(255, rgba[11]);     // colour stays opaque
        Assert.Equal(200, rgba[8]);      // rgb preserved
    }

    [Fact]
    public void Bmp_ReturnsPixelsInTopDownRowOrder()
    {
        byte[] payload = BmpBytes(
            2,
            new SKColor(255, 0, 0),
            new SKColor(0, 255, 0),
            new SKColor(0, 0, 255),
            new SKColor(255, 255, 255));

        byte[] rgba = PayloadDecoder.ToRgba(payload, out int width, out int height);

        Assert.Equal((2, 2), (width, height));
        Assert.Equal(
            new byte[]
            {
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255,
            },
            rgba);
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
