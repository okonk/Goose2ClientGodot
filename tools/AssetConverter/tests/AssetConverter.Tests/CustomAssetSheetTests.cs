using System.Text.Json;
using Goose2.AssetConverter.Manifest;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetConverter.Tests;

public class CustomAssetSheetTests
{
    [Fact]
    public void Write_PacksQuestionAndExclamationIntoReservedSheet()
    {
        string root = Path.Combine(Path.GetTempPath(), $"custom-assets-{Guid.NewGuid():N}");
        string source = Path.Combine(root, "source");
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(source);

        try
        {
            using (var question = new Image<Rgba32>(32, 32, Color.Magenta))
                question.SaveAsPng(Path.Combine(source, "question.png"));
            using (var exclamation = new Image<Rgba32>(32, 32, Color.Yellow))
                exclamation.SaveAsPng(Path.Combine(source, "exclamation.png"));

            CustomAssetSheet.Write(source, output);

            using var sheet = Image.Load<Rgba32>(Path.Combine(output, "30000.png"));
            Assert.Equal(64, sheet.Width);
            Assert.Equal(32, sheet.Height);
            Assert.Equal(new Rgba32(255, 255, 0), sheet[0, 0]);
            Assert.Equal(new Rgba32(255, 0, 255), sheet[32, 0]);
        }
        finally
        {
            Directory.Delete(root, true);
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
