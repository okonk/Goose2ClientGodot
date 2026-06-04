using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Gif;
using Goose2.AssetConverter.Png;

namespace Goose2.AssetConverter;

/// <summary>Result of a batch conversion run.</summary>
public record BatchResult(int Succeeded, int Failed, IReadOnlyList<string> Failures);

/// <summary>Batch-converts graphic .adf files to PNG.</summary>
public static class BatchConverter
{
    /// <summary>Decodes every graphic .adf in <paramref name="dataDir"/> to
    /// <paramref name="outDir"/>/&lt;fileNumber&gt;.png. Non-graphic or undecodable files are
    /// counted as failures and listed, never silently skipped.</summary>
    public static BatchResult Convert(string dataDir, string outDir, int[]? onlyFileNumbers = null)
    {
        Directory.CreateDirectory(outDir);
        var failures = new List<string>();
        int ok = 0, fail = 0;

        var only = onlyFileNumbers is null ? null : new HashSet<int>(onlyFileNumbers);

        foreach (var file in Directory.EnumerateFiles(dataDir, "*.adf"))
        {
            int fileNumber = int.Parse(Path.GetFileNameWithoutExtension(file));
            if (only is not null && !only.Contains(fileNumber)) continue;

            try
            {
                var adf = new AdfFile(file);
                if (adf.Type != AdfType.Graphic)
                {
                    fail++;
                    failures.Add($"{fileNumber}: not a graphic ({adf.Type})");
                    continue;
                }

                var rgba = GifLoader.Load(adf.FileData, out int w, out int h);
                PngWriter.Write(rgba, w, h, Path.Combine(outDir, $"{fileNumber}.png"));
                ok++;
            }
            catch (Exception e)
            {
                fail++;
                failures.Add($"{fileNumber}: {e.GetType().Name} {e.Message}");
            }
        }

        return new BatchResult(ok, fail, failures);
    }
}
