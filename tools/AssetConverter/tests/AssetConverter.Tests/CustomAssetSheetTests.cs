using System.Text.Json;
using Goose2.AssetConverter.Manifest;
using SkiaSharp;

namespace AssetConverter.Tests;

public class CustomAssetSheetTests
{
    private static void WriteIcon(string path, params (int X, int Y, SKColor Color)[] pixels)
    {
        using SKBitmap image = new(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        image.Erase(SKColors.Transparent);
        foreach ((int x, int y, SKColor color) in pixels)
        {
            image.SetPixel(x, y, color);
        }

        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream output = File.Create(path);
        encoded.SaveTo(output);
    }

    [Fact]
    public void Write_PacksQuestionAndExclamationIntoReservedSheet()
    {
        string root = Path.Combine(Path.GetTempPath(), $"custom-assets-{Guid.NewGuid():N}");
        string source = Path.Combine(root, "source");
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(source);

        try
        {
            SKColor translucentRed = new(255, 0, 0, 128);
            WriteIcon(
                Path.Combine(source, "exclamation.png"),
                (0, 0, SKColors.Yellow),
                (31, 0, translucentRed),
                (0, 31, SKColors.Blue),
                (31, 31, SKColors.Green));
            WriteIcon(
                Path.Combine(source, "question.png"),
                (0, 0, SKColors.Magenta),
                (31, 0, SKColors.Cyan),
                (0, 31, SKColors.Black),
                (31, 31, SKColors.White));

            CustomAssetSheet.Write(source, output);

            using SKBitmap sheet = SKBitmap.Decode(Path.Combine(output, "30000.png"))!;
            Assert.Equal(64, sheet.Width);
            Assert.Equal(32, sheet.Height);
            Assert.Equal(SKColors.Yellow, sheet.GetPixel(0, 0));
            Assert.Equal(translucentRed, sheet.GetPixel(31, 0));
            Assert.Equal(SKColors.Blue, sheet.GetPixel(0, 31));
            Assert.Equal(SKColors.Green, sheet.GetPixel(31, 31));
            Assert.Equal(SKColors.Magenta, sheet.GetPixel(32, 0));
            Assert.Equal(SKColors.Cyan, sheet.GetPixel(63, 0));
            Assert.Equal(SKColors.Black, sheet.GetPixel(32, 31));
            Assert.Equal(SKColors.White, sheet.GetPixel(63, 31));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Write_ClearsHiddenColorOfFullyTransparentPixels()
    {
        string output = Path.Combine(Path.GetTempPath(), $"custom-assets-{Guid.NewGuid():N}");

        try
        {
            CustomAssetSheet.Write(Goose2.AssetConverter.Paths.CustomAssetsDir, output);

            SKImageInfo unpremul = new(64, 32, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using SKBitmap sheet = SKBitmap.Decode(Path.Combine(output, "30000.png"), unpremul)!;
            ReadOnlySpan<byte> pixels = sheet.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] == 0)
                {
                    Assert.Equal(0, pixels[i] | pixels[i + 1] | pixels[i + 2]);
                }
            }
        }
        finally
        {
            Directory.Delete(output, true);
        }
    }

    [Fact]
    public void FrameManifest_ContainsCustomAssetRects()
    {
        string root = Path.Combine(Path.GetTempPath(), $"custom-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            using var json = JsonDocument.Parse(FrameManifestBuilder.Build(root));
            var sheet = json.RootElement.GetProperty("sheets").GetProperty("30000");
            Assert.Equal(new[] { 0, 0, 32, 32 }, sheet.GetProperty("1").EnumerateArray().Select(x => x.GetInt32()));
            Assert.Equal(new[] { 32, 0, 32, 32 }, sheet.GetProperty("2").EnumerateArray().Select(x => x.GetInt32()));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
