using Goose2.AssetConverter;
using Goose2.AssetConverter.Aspereta;
using Goose2.AssetConverter.Manifest;
using Goose2.AssetConverter.Maps;
using Goose2.AssetConverter.SpriteFrames;

static string ResolveAsperetaMappingPath(string? repoRoot = null)
{
    var candidates = new List<string>
    {
        // From tools/AssetConverter/src/AssetConverter (dotnet run CWD)
        Path.GetFullPath(Path.Combine("..", "..", "data", "aspereta-mapping.tsv")),
        // From tools/AssetConverter
        Path.GetFullPath(Path.Combine("data", "aspereta-mapping.tsv")),
    };

    if (!string.IsNullOrEmpty(repoRoot))
    {
        candidates.Add(Path.GetFullPath(Path.Combine(
            repoRoot, "tools", "AssetConverter", "data", "aspereta-mapping.tsv")));
    }

    string? loc = Path.GetDirectoryName(typeof(Program).Assembly.Location);
    if (loc is not null)
    {
        // bin/Debug/netX.Y -> tools/AssetConverter
        candidates.Add(Path.GetFullPath(Path.Combine(
            loc, "..", "..", "..", "..", "..", "data", "aspereta-mapping.tsv")));
    }

    return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
}

if (args.Length >= 1 && args[0] == "aspereta-mapping")
{
    string outPath = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("..", "..", "data", "aspereta-mapping.tsv"));

    var rows = AsperetaMapping.Build(Paths.IllutiaData, Paths.AsperetaData);
    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
    File.WriteAllText(outPath, AsperetaMapping.ToTsv(rows));

    int matched = rows.Count(r => r.Status == MappingStatus.Matched);
    int inject = rows.Count(r => r.Status == MappingStatus.Inject);
    Console.WriteLine($"Wrote {rows.Count} rows ({matched} matched, {inject} inject) -> {outPath}");
    return;
}

if (args.Length >= 1 && args[0] == "aspereta")
{
    string repoRoot = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("..", ".."));

    string mappingPath = ResolveAsperetaMappingPath(repoRoot);
    var mapping = AsperetaMapping.FromTsv(mappingPath);

    var sheets = AsperetaBatchConverter.Convert(
        Paths.AsperetaData, Path.Combine(repoRoot, "Assets", "Sprites", "sheets"));
    var maps = AsperetaMapConverter.Convert(
        Paths.AsperetaMaps, Path.Combine(repoRoot, "Assets", "Maps"), mapping);

    Console.WriteLine($"Aspereta sheets: {sheets.Succeeded} ok, {sheets.Failed} failed");
    Console.WriteLine($"Aspereta maps: {maps.Converted} converted, {maps.Failures.Count} failed, {maps.Warnings.Count} warnings");
    foreach (var w in maps.Warnings) Console.WriteLine($"  WARN {w}");
    foreach (var f in sheets.Failures.Concat(maps.Failures)) Console.WriteLine($"  FAIL {f}");
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
    string tres = SpriteFramesWriter.Build(
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
    string mappingPath = ResolveAsperetaMappingPath(repoRoot);

    // Illutia sheets
    var sheets = BatchConverter.Convert(Paths.IllutiaData, sheetsDir);

    // Aspereta monsters (metadata via Convert; .tres written separately)
    var aspSheetsInfo = AsperetaSheets.Load(Paths.AsperetaData);
    var monsterResources = AsperetaMonsterConverter.BuildResources(
        AsperetaCompiledEnc.Load(Paths.AsperetaCompiledEnc),
        aspSheetsInfo, out var monsterErrors);

    var animations = AnimationBatchConverter.Convert(
        Paths.IllutiaData, Paths.CompiledEnc, repoRoot, includeEffects: true,
        extraResources: monsterResources);

    // Monster .tres files are not written by Convert's illutia loop — write them here
    int monstersWritten = 0;
    foreach (var resource in monsterResources.Where(r => r.Animations.Count > 0))
    {
        string fullPath = Path.Combine(repoRoot, resource.RelativeOutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, SpriteFramesWriter.Build(resource.Animations));
        monstersWritten++;
    }

    // Illutia maps
    var maps = MapCopyConverter.Convert(Paths.IllutiaMaps, mapsDir);

    // Aspereta sheets + maps
    var aspBatch = AsperetaBatchConverter.Convert(Paths.AsperetaData, sheetsDir);
    var mappingRows = AsperetaMapping.FromTsv(mappingPath);
    var aspMaps = AsperetaMapConverter.Convert(Paths.AsperetaMaps, mapsDir, mappingRows);

    // Combined frame manifest
    string manifestPath = Path.Combine(repoRoot, "Assets", "Sprites", "manifest.json");
    Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
    File.WriteAllText(manifestPath,
        FrameManifestBuilder.BuildCombined(Paths.IllutiaData, Paths.AsperetaData));

    Console.WriteLine($"Sheets: {sheets.Succeeded} ok, {sheets.Failed} failed");
    Console.WriteLine($"Animations: {animations.ResourcesWritten} character, {animations.EffectsWritten} effects, {animations.Failed} failed");
    Console.WriteLine($"Aspereta monsters: {monstersWritten} written, {monsterErrors.Count} errors");
    foreach (var e in monsterErrors) Console.WriteLine($"  MONSTER {e}");
    Console.WriteLine($"Maps: {maps.Copied} copied, {maps.Failures.Count} failed");
    Console.WriteLine($"Aspereta sheets: {aspBatch.Succeeded} ok, {aspBatch.Failed} failed");
    Console.WriteLine($"Aspereta maps: {aspMaps.Converted} converted, {aspMaps.Failures.Count} failed, {aspMaps.Warnings.Count} warnings");
    foreach (var w in aspMaps.Warnings) Console.WriteLine($"  WARN {w}");
    foreach (var f in aspBatch.Failures.Concat(aspMaps.Failures)) Console.WriteLine($"  FAIL {f}");
    Console.WriteLine($"Manifest: {manifestPath}");
    return;
}

Console.WriteLine("Usage: AssetConverter batch [outDir] | frames <id> | animations [repoRoot] | maps [outDir] | manifest [outPath] | aspereta-mapping [outPath] | aspereta [repoRoot] | all [repoRoot]");
