using Goose2.AssetConverter.Png;

namespace Goose2.AssetConverter.Aspereta;

/// <summary>Converts every Aspereta graphic sheet to a renumbered, alpha-keyed PNG.
/// Output name = the sheet's assigned NewSheetNumber (see AsperetaSheets).</summary>
public static class AsperetaBatchConverter
{
    public static BatchResult Convert(string asperetaDataDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var failures = new List<string>();
        int ok = 0;

        foreach (var (fileNumber, sheet) in AsperetaSheets.Load(asperetaDataDir))
        {
            try
            {
                var rgba = PayloadDecoder.ToRgba(sheet.Adf.FileData, out int w, out int h);
                PngWriter.Write(rgba, w, h, Path.Combine(outDir, $"{sheet.NewSheetNumber}.png"));
                ok++;
            }
            catch (Exception e)
            {
                failures.Add($"{fileNumber}: {e.GetType().Name} {e.Message}");
            }
        }
        return new BatchResult(ok, failures.Count, failures);
    }
}
