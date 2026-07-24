using Goose2.AssetConverter;
using Goose2.AssetConverter.Aspereta;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaMapConverterTests
{
    private static readonly string MappingTsv = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../../data/aspereta-mapping.tsv"));

    [Fact]
    public void ConvertsAllMaps_RoundTripParsesInGoose2Format()
    {
        string outDir = Path.Combine(Path.GetTempPath(), $"asp-maps-{Guid.NewGuid():N}");
        try
        {
            var mapping = AsperetaMapping.FromTsv(MappingTsv);
            var result = AsperetaMapConverter.Convert(Paths.AsperetaMaps, outDir, mapping);

            Assert.Equal(44, result.Converted);
            Assert.Empty(result.Failures);
            Assert.True(File.Exists(Path.Combine(outDir, "Map10001.bytes")));

            var bytes = File.ReadAllBytes(Path.Combine(outDir, "Map10001.bytes"));
            using var r = new BinaryReader(new MemoryStream(bytes));
            short version = r.ReadInt16(); short editorVersion = r.ReadInt16();
            int width = r.ReadInt32(); int height = r.ReadInt32();
            Assert.Equal((100, 100), (width, height));

            int nonEmptyLayers = 0;
            for (int i = 0; i < width * height; i++)
            {
                int flags = r.ReadInt32();
                Assert.True(flags is 0 or 2);
                for (int k = 0; k < 5; k++)
                {
                    int graphic = r.ReadInt32();
                    short sheet = r.ReadInt16();
                    if (graphic == 0) { Assert.Equal(0, sheet); continue; }
                    nonEmptyLayers++;
                    Assert.True(k != 3, "layer 3 must be empty");
                    Assert.True(sheet is (>= 0 and <= 4962) or (>= 20000 and <= 20461));
                }
            }
            Assert.Equal(bytes.Length, r.BaseStream.Position);
            Assert.True(nonEmptyLayers > 0);
        }
        finally { Directory.Delete(outDir, recursive: true); }
    }
}
