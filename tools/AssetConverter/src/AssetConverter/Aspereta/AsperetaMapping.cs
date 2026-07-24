using System.Security.Cryptography;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Png;

namespace Goose2.AssetConverter.Aspereta;

public enum MappingStatus { Matched, Inject }

public sealed record MappingRow(
    int AspSheet, int AspGraphic, MappingStatus Status, int OutSheet, int OutGraphic);

public static class AsperetaMapping
{
    public static List<MappingRow> Build(string illutiaDataDir, string asperetaDataDir)
    {
        var illutiaIndex = new Dictionary<string, (int Graphic, int Sheet)>();
        foreach (var file in Directory.EnumerateFiles(illutiaDataDir, "*.adf").OrderBy(f => f))
        {
            AdfFile adf;
            try { adf = new AdfFile(file); }
            catch { continue; }
            if (adf.Type != AdfType.Graphic || adf.Frames.Count == 0) continue;

            foreach (var (frame, hash) in FrameHashes(adf))
                illutiaIndex.TryAdd(hash, (frame.Index, adf.FileNumber));
        }

        var rows = new List<MappingRow>();
        foreach (var (fileNumber, sheet) in AsperetaSheets.Load(asperetaDataDir).OrderBy(kv => kv.Key))
        {
            foreach (var (frame, hash) in FrameHashes(sheet.Adf))
            {
                rows.Add(illutiaIndex.TryGetValue(hash, out var donor)
                    ? new MappingRow(fileNumber, frame.Index, MappingStatus.Matched, donor.Sheet, donor.Graphic)
                    : new MappingRow(fileNumber, frame.Index, MappingStatus.Inject,
                        sheet.NewSheetNumber, AsperetaSheets.GraphicBase + frame.Index));
            }
        }
        return rows;
    }

    private static IEnumerable<(Frame Frame, string Hash)> FrameHashes(AdfFile adf)
    {
        byte[] rgba;
        int width, height;
        try { rgba = PayloadDecoder.ToRgba(adf.FileData, out width, out height); }
        catch { yield break; }

        foreach (var f in adf.Frames)
        {
            if (f.W <= 0 || f.H <= 0 || f.X < 0 || f.Y < 0 ||
                f.X + f.W > width || f.Y + f.H > height) continue;

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> dims = stackalloc byte[8];
            BitConverter.TryWriteBytes(dims[..4], f.W);
            BitConverter.TryWriteBytes(dims[4..], f.H);
            sha.AppendData(dims);
            for (int row = 0; row < f.H; row++)
                sha.AppendData(rgba, ((f.Y + row) * width + f.X) * 4, f.W * 4);
            yield return (f, Convert.ToHexString(sha.GetHashAndReset()));
        }
    }

    public static string ToTsv(IEnumerable<MappingRow> rows)
    {
        var sb = new System.Text.StringBuilder("asp_sheet\tasp_graphic\tstatus\tout_sheet\tout_graphic\n");
        foreach (var r in rows)
            sb.Append($"{r.AspSheet}\t{r.AspGraphic}\t{r.Status.ToString().ToLowerInvariant()}\t{r.OutSheet}\t{r.OutGraphic}\n");
        return sb.ToString();
    }

    public static List<MappingRow> FromTsv(string path) =>
        File.ReadLines(path).Skip(1).Where(l => l.Length > 0).Select(l =>
        {
            var f = l.Split('\t');
            return new MappingRow(int.Parse(f[0]), int.Parse(f[1]),
                f[2] == "matched" ? MappingStatus.Matched : MappingStatus.Inject,
                int.Parse(f[3]), int.Parse(f[4]));
        }).ToList();
}
