namespace Goose2.AssetConverter.Maps;

/// <summary>Result of a map-copy conversion run.</summary>
public sealed record MapCopyResult(int Copied, IReadOnlyList<string> Failures);

/// <summary>Copies *.map files from a source directory into a Godot-friendly layout.
/// Unity naming rule: <c>Map100.map</c> → <c>Map100.bytes</c> (M + basename[1:] + .bytes).</summary>
public static class MapCopyConverter
{
    /// <summary>Copies all <c>*.map</c> files from <paramref name="sourceMapsDir"/> into
    /// <paramref name="outMapsDir"/> using the Unity Godot naming convention.</summary>
    public static MapCopyResult Convert(string sourceMapsDir, string outMapsDir)
    {
        var failures = new List<string>();
        int copied = 0;

        Directory.CreateDirectory(outMapsDir);

        var mapFiles = Directory.EnumerateFiles(sourceMapsDir, "*.map", SearchOption.TopDirectoryOnly);

        foreach (var file in mapFiles)
        {
            try
            {
                var basename = Path.GetFileNameWithoutExtension(file);

                // Validate: need at least 2 characters so Substring(1) produces non-empty output name
                if (basename.Length < 2)
                {
                    failures.Add($"{Path.GetFileName(file)}: invalid short basename \"{basename}\"");
                    continue;
                }

                // Unity naming rule: M + rest_of_basename + .bytes
                // e.g. Map100.map → M + ap100 + .bytes → Map100.bytes
                var godotName = $"M{basename.Substring(1)}.bytes";
                var outPath = Path.Combine(outMapsDir, godotName);

                File.Copy(file, outPath, overwrite: true);
                copied++;
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return new MapCopyResult(copied, failures);
    }
}
