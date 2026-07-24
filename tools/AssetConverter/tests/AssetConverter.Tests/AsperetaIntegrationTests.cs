using System.Text.Json;
using Goose2.AssetConverter;
using Goose2.AssetConverter.Aspereta;
using Goose2.AssetConverter.Manifest;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaIntegrationTests
{
    [Fact]
    public void EveryConvertedMapReference_ResolvesInCombinedManifestAndSheets()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"asp-integ-{Guid.NewGuid():N}");
        try
        {
            string mappingPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "../../../../../data/aspereta-mapping.tsv"));
            var mapping = AsperetaMapping.FromTsv(mappingPath);

            var maps = AsperetaMapConverter.Convert(Paths.AsperetaMaps, tmp, mapping);
            Assert.Equal(44, maps.Converted);

            using var manifest = JsonDocument.Parse(
                FrameManifestBuilder.BuildCombined(Paths.IllutiaData, Paths.AsperetaData));
            var sheets = manifest.RootElement.GetProperty("sheets");

            var missing = new List<string>();
            foreach (var mapFile in Directory.EnumerateFiles(tmp, "*.bytes"))
            {
                using var r = new BinaryReader(File.OpenRead(mapFile));
                r.ReadInt16(); r.ReadInt16(); int w = r.ReadInt32(); int h = r.ReadInt32();
                for (int i = 0; i < w * h; i++)
                {
                    r.ReadInt32(); // flags
                    for (int k = 0; k < 5; k++)
                    {
                        int graphic = r.ReadInt32(); short sheet = r.ReadInt16();
                        if (graphic == 0) continue;
                        if (!sheets.TryGetProperty(sheet.ToString(), out var sheetObj) ||
                            !sheetObj.TryGetProperty(graphic.ToString(), out _))
                            missing.Add($"{Path.GetFileName(mapFile)}: ({sheet},{graphic})");
                    }
                }
            }
            Assert.Empty(missing.Distinct().Take(20).ToList());
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
