using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Gif;
using Goose2.AssetConverter.Png;
using SixLabors.ImageSharp;

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

                var outPath = Path.Combine(outDir, $"{fileNumber}.png");
                DecodeGraphicPayload(adf.FileData, outPath);
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

    /// <summary>Decodes a graphic ADF payload to PNG, dispatching on payload format.</summary>
    /// <param name="payload">The decoded file data from the ADF.</param>
    /// <param name="outPath">Path to write the PNG.</param>
    /// <exception cref="NotSupportedException">When the payload format is not GIF or BMP.</exception>
    private static void DecodeGraphicPayload(byte[] payload, string outPath)
    {
        if (payload.Length < 3)
            throw new NotSupportedException("Payload too short to determine format");

        // GIF: signature starts with "GIF" (GIF87a / GIF89a)
        if (payload[0] == 'G' && payload[1] == 'I' && payload[2] == 'F')
        {
            var rgba = GifLoader.Load(payload, out int w, out int h);
            PngWriter.Write(rgba, w, h, outPath);
            return;
        }

        // BMP: signature starts with "BM"
        if (payload[0] == 'B' && payload[1] == 'M')
        {
            using var image = Image.Load(payload);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            image.SaveAsPng(outPath);
            return;
        }

        throw new NotSupportedException(
            $"Unsupported graphic payload format (bytes: 0x{payload[0]:X2} 0x{payload[1]:X2} 0x{payload[2]:X2}). Expected GIF or BMP.");
    }
}
