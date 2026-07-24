namespace Goose2.AssetConverter.Aspereta;

public sealed record MapConvertResult(int Converted, IReadOnlyList<string> Failures,
    IReadOnlyList<string> Warnings);

public static class AsperetaMapConverter
{
    public static MapConvertResult Convert(
        string asperetaMapsDir, string outDir, IReadOnlyList<MappingRow> mapping)
    {
        Directory.CreateDirectory(outDir);
        var failures = new List<string>();
        var warnings = new List<string>();
        var byGraphic = mapping.ToDictionary(m => m.AspGraphic);
        int converted = 0;
        Span<int> asp = stackalloc int[4];

        foreach (var file in Directory.EnumerateFiles(asperetaMapsDir, "Map*.map"))
        {
            try
            {
                string basename = Path.GetFileNameWithoutExtension(file);
                int number = int.Parse(basename["Map".Length..]);
                string outPath = Path.Combine(outDir,
                    $"Map{AsperetaSheets.MapNumberBase + number}.bytes");

                using var reader = new BinaryReader(File.OpenRead(file));
                using var writer = new BinaryWriter(File.Create(outPath));

                writer.Write(reader.ReadInt16());
                writer.Write(reader.ReadInt16());
                writer.Write(100); writer.Write(100);

                for (int i = 0; i < 100 * 100; i++)
                {
                    byte blocked = reader.ReadByte();
                    writer.Write(blocked == 1 ? 2 : 0);

                    for (int k = 0; k < 4; k++) asp[k] = reader.ReadInt32();

                    for (int outLayer = 0; outLayer < 5; outLayer++)
                    {
                        int src = outLayer switch { 0 => 0, 1 => 1, 2 => 2, 3 => -1, 4 => 3, _ => -1 };
                        int graphic = src < 0 ? 0 : asp[src];

                        if (graphic == 0) { writer.Write(0); writer.Write((short)0); continue; }

                        if (byGraphic.TryGetValue(graphic, out var row))
                        {
                            writer.Write(row.OutGraphic);
                            writer.Write((short)row.OutSheet);
                        }
                        else
                        {
                            warnings.Add($"{basename}: graphic {graphic} not in mapping table, dropped");
                            writer.Write(0); writer.Write((short)0);
                        }
                    }
                }
                converted++;
            }
            catch (Exception e)
            {
                failures.Add($"{Path.GetFileName(file)}: {e.GetType().Name} {e.Message}");
            }
        }
        return new MapConvertResult(converted, failures, warnings.Distinct().ToList());
    }
}
