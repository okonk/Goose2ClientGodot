using Goose2.AssetConverter;
using Goose2.AssetConverter.Aspereta;
using SkiaSharp;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaBatchConverterTests
{
    [Fact]
    public void ConvertsAllSheetsWithRenumberedNamesAndTransparency()
    {
        string outDir = Path.Combine(Path.GetTempPath(), $"asp-batch-{Guid.NewGuid():N}");
        try
        {
            var result = AsperetaBatchConverter.Convert(Paths.AsperetaData, outDir);

            Assert.Equal(462, result.Succeeded); // local graphics count (not plan's 487)
            Assert.Empty(result.Failures);
            Assert.Equal(462, Directory.EnumerateFiles(outDir, "*.png").Count());
            Assert.True(File.Exists(Path.Combine(outDir, "20000.png")));

            var sheets = AsperetaSheets.Load(Paths.AsperetaData);
            using SKBitmap img = SKBitmap.Decode(
                Path.Combine(outDir, $"{sheets[1].NewSheetNumber}.png"))!;
            Assert.Equal(0, img.GetPixel(0, img.Height - 1).Alpha);   // corner background pixel transparent
        }
        finally { Directory.Delete(outDir, recursive: true); }
    }
}
