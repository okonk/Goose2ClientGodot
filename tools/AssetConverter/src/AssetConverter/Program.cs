using Goose2.AssetConverter;
using Goose2.AssetConverter.Aspereta;
using Goose2.AssetConverter.Manifest;
using Goose2.AssetConverter.Maps;
using Goose2.AssetConverter.SpriteFrames;

if (args.Length >= 1 && args[0] == "aspereta-mapping")
{
    string outPath = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("data", "aspereta-mapping.tsv"));

    var rows = AsperetaMapping.Build(Paths.IllutiaData, Paths.AsperetaData);
    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
    File.WriteAllText(outPath, AsperetaMapping.ToTsv(rows));

    int matched = rows.Count(r => r.Status == MappingStatus.Matched);
    int inject = rows.Count(r => r.Status == MappingStatus.Inject);
    Console.WriteLine($"Wrote {rows.Count} rows ({matched} matched, {inject} inject) -> {outPath}");
    return;
}

if (args.Length >= 1 && args[0] == "animations")
{
    string outRoot = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("..", ".."));

    var result = AnimationBatchConverter.Convert(
        Paths.IllutiaData, Paths.CompiledEnc, outRoot, includeEffects: true);

    Console.WriteLine($"Wrote {result.ResourcesWritten} animation resources, {result.Failed} failures -> {outRoot}");
    foreach (var w in result.Warnings) Console.WriteLine($"  WARN {w}");
    foreach (var f in result.Failures) Console.WriteLine($"  FAIL {f}");
    return;
}

if (args.Length >= 1 && args[0] == "batch")
{
    string outDir = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("..", "..", "Assets", "Sprites", "sheets"));

    var result = BatchConverter.Convert(Paths.IllutiaData, outDir);
    Console.WriteLine($"Converted {result.Succeeded} sheets, {result.Failed} failures -> {outDir}");
    foreach (var f in result.Failures) Console.WriteLine($"  SKIP {f}");
    return;
}

if (args.Length >= 2 && args[0] == "frames")
{
    int id = int.Parse(args[1]);
    var adf = new Goose2.AssetConverter.Adf.AdfFile(Paths.Adf(id));
    string tres = Goose2.AssetConverter.SpriteFrames.SpriteFramesWriter.Build(
        adf, $"res://Assets/Sprites/sheets/{id}.png");
    string outPath = Path.GetFullPath(Path.Combine("..", "..", "Assets", "Sprites", "sheets", $"{id}.frames.tres"));
    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
    File.WriteAllText(outPath, tres);
    Console.WriteLine($"Wrote {outPath}");
    return;
}

if (args.Length >= 1 && args[0] == "maps")
{
    string outDir = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("..", "..", "Assets", "Maps"));

    var result = MapCopyConverter.Convert(Paths.IllutiaMaps, outDir);
    Console.WriteLine($"Copied {result.Copied} maps -> {outDir}");
    foreach (var f in result.Failures) Console.WriteLine($"  FAIL {f}");
    return;
}

if (args.Length >= 1 && args[0] == "manifest")
{
    string outPath = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("..", "..", "Assets", "Sprites", "manifest.json"));
    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
    File.WriteAllText(outPath, FrameManifestBuilder.Build(Paths.IllutiaData));
    Console.WriteLine($"Wrote {outPath}");
    return;
}

if (args.Length >= 1 && args[0] == "all")
{
    string repoRoot = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("..", ".."));

    var sheetsDir = Path.Combine(repoRoot, "Assets", "Sprites", "sheets");
    var mapsDir = Path.Combine(repoRoot, "Assets", "Maps");

    var sheets = BatchConverter.Convert(Paths.IllutiaData, sheetsDir);
    var animations = AnimationBatchConverter.Convert(
        Paths.IllutiaData, Paths.CompiledEnc, repoRoot, includeEffects: true);
    var maps = MapCopyConverter.Convert(Paths.IllutiaMaps, mapsDir);

    string manifestPath = Path.Combine(repoRoot, "Assets", "Sprites", "manifest.json");
    Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
    File.WriteAllText(manifestPath, FrameManifestBuilder.Build(Paths.IllutiaData));

    Console.WriteLine($"Sheets: {sheets.Succeeded} ok, {sheets.Failed} failed");
    Console.WriteLine($"Animations: {animations.ResourcesWritten} character, {animations.EffectsWritten} effects, {animations.Failed} failed");
    Console.WriteLine($"Maps: {maps.Copied} copied, {maps.Failures.Count} failed");
    Console.WriteLine($"Manifest: {manifestPath}");
    return;
}

Console.WriteLine("Usage: AssetConverter batch [outDir] | frames <id> | animations [repoRoot] | maps [outDir] | manifest [outPath] | aspereta-mapping [outPath] | all [repoRoot]");
