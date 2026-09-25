using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;
using Goose2.AssetConverter.Manifest;
using Goose2.AssetConverter.Maps;
using Goose2.AssetConverter.Tiles;
using Goose2.AssetConverter.SpriteFrames;

static int PublishIfChanged(string path, string content)
{
    if (File.Exists(path) && File.ReadAllText(path) == content)
    {
        Console.WriteLine($"  unchanged {path}");
        return 0;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
    Console.WriteLine($"  wrote     {path}");
    return 1;
}

static Dictionary<int, AdfFile> LoadIllutiaAdfs(string dataDir, out int failed)
{
    var adfs = new Dictionary<int, AdfFile>();
    failed = 0;
    foreach (var file in Directory.EnumerateFiles(dataDir, "*.adf"))
    {
        try
        {
            var adf = new AdfFile(file);
            adfs[adf.FileNumber] = adf;
        }
        catch
        {
            failed++;
        }
    }
    return adfs;
}

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
    var fx = AsperetaEffectsConverter.Convert(
        Paths.AsperetaData, Paths.AsperetaCompiledEnc, repoRoot);

    Console.WriteLine($"Aspereta sheets: {sheets.Succeeded} ok, {sheets.Failed} failed");
    Console.WriteLine($"Aspereta maps: {maps.Converted} converted, {maps.Failures.Count} failed, {maps.Warnings.Count} warnings");
    Console.WriteLine($"Aspereta effects: {fx.EffectsWritten} written, {fx.Failed} failed, {fx.SkippedOutOfRange} out-of-range skipped");
    foreach (var w in maps.Warnings) Console.WriteLine($"  WARN {w}");
    foreach (var f in sheets.Failures.Concat(maps.Failures).Concat(fx.Failures))
        Console.WriteLine($"  FAIL {f}");
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
    Console.WriteLine($"Appearance manifest: {Path.Combine(outRoot, AppearanceManifestFileStore.RelativePath)}");
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
    CustomAssetSheet.Write(Paths.CustomAssetsDir, outDir);
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
    ManifestFileStore.Write(outPath,
        () => FrameManifestBuilder.Build(Paths.IllutiaData),
        () => AnimationManifestBuilder.Build(Paths.IllutiaData, Paths.CompiledEnc, Paths.ItemTileSheets));
    CustomAssetSheet.Write(Paths.CustomAssetsDir,
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outPath))!, "sheets"));
    Console.WriteLine($"Wrote {outPath}");
    Console.WriteLine($"Wrote {Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outPath))!, ManifestFileStore.AnimationFileName)}");
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
    CustomAssetSheet.Write(Paths.CustomAssetsDir, sheetsDir);

    // Aspereta animations (metadata via Convert; .tres written separately)
    var aspBuild = AsperetaAnimationResourceBuilder.Build(
        AsperetaAnimationCatalog.Load(Paths.AsperetaData, Paths.AsperetaCompiledEnc));

    var animations = AnimationBatchConverter.Convert(
        Paths.IllutiaData, Paths.CompiledEnc, repoRoot, includeEffects: true,
        extraResources: aspBuild.Resources);

    // Aspereta .tres files are not written by Convert's illutia loop — write them here
    int aspWritten = 0;
    foreach (var resource in aspBuild.Resources.Where(r => r.Animations.Count > 0))
    {
        string fullPath = Path.Combine(repoRoot, resource.RelativeOutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, SpriteFramesWriter.Build(resource.Animations));
        aspWritten++;
    }

    // Illutia maps
    var maps = MapCopyConverter.Convert(Paths.IllutiaMaps, mapsDir);

    // Aspereta sheets + maps
    var aspBatch = AsperetaBatchConverter.Convert(Paths.AsperetaData, sheetsDir);
    var mappingRows = AsperetaMapping.FromTsv(mappingPath);
    var aspMaps = AsperetaMapConverter.Convert(Paths.AsperetaMaps, mapsDir, mappingRows);

    // Aspereta effects (after Illutia animation metadata so heights merge preserves it)
    var fx = AsperetaEffectsConverter.Convert(
        Paths.AsperetaData, Paths.AsperetaCompiledEnc, repoRoot);

    // Combined frame + animation manifests
    ManifestFileStore.WriteCombined(repoRoot,
        () => FrameManifestBuilder.BuildCombined(Paths.IllutiaData, Paths.AsperetaData),
        () => AnimationManifestBuilder.BuildCombined(
            Paths.IllutiaData, Paths.CompiledEnc, Paths.AsperetaData, Paths.AsperetaCompiledEnc,
            Paths.ItemTileSheets));

    Console.WriteLine($"Sheets: {sheets.Succeeded} ok, {sheets.Failed} failed");
    Console.WriteLine($"Animations: {animations.ResourcesWritten} character, {animations.EffectsWritten} effects, {animations.Failed} failed");
    Console.WriteLine($"Aspereta animations: {aspWritten} written, {aspBuild.Diagnostics.Count} diagnostics");
    foreach (var e in aspBuild.Diagnostics) Console.WriteLine($"  ASPERETA {e}");
    Console.WriteLine($"Maps: {maps.Copied} copied, {maps.Failures.Count} failed");
    Console.WriteLine($"Aspereta sheets: {aspBatch.Succeeded} ok, {aspBatch.Failed} failed");
    Console.WriteLine($"Aspereta maps: {aspMaps.Converted} converted, {aspMaps.Failures.Count} failed, {aspMaps.Warnings.Count} warnings");
    Console.WriteLine($"Aspereta effects: {fx.EffectsWritten} written, {fx.Failed} failed, {fx.SkippedOutOfRange} out-of-range skipped");
    foreach (var w in aspMaps.Warnings) Console.WriteLine($"  WARN {w}");
    foreach (var f in aspBatch.Failures.Concat(aspMaps.Failures).Concat(fx.Failures))
        Console.WriteLine($"  FAIL {f}");
    Console.WriteLine($"Manifest: {Path.Combine(repoRoot, "Assets", "Sprites", "manifest.json")}");
    Console.WriteLine($"Animation manifest: {Path.Combine(repoRoot, "Assets", "Sprites", ManifestFileStore.AnimationFileName)}");
    Console.WriteLine($"Appearance manifest: {Path.Combine(repoRoot, AppearanceManifestFileStore.RelativePath)}");
    return;
}

if (args.Length >= 2 && args[0] == "aspereta-body")
{
    int bodyId = int.Parse(args[1]);
    string repoRoot = args.Length >= 3
        ? Path.GetFullPath(args[2])
        : Path.GetFullPath(Path.Combine("..", ".."));

    var aspBuild = AsperetaAnimationResourceBuilder.Build(
        AsperetaAnimationCatalog.Load(Paths.AsperetaData, Paths.AsperetaCompiledEnc));

    int outputBodyId = AsperetaSheets.BodyBase + bodyId;
    var target = aspBuild.Resources.FirstOrDefault(r => r.Id == outputBodyId && r.Animations.Count > 0);
    if (target is null)
    {
        Console.WriteLine($"Aspereta body {bodyId}: no resource for output body {outputBodyId}; nothing written");
        foreach (var e in aspBuild.Diagnostics) Console.WriteLine($"  ASPERETA {e}");
        return;
    }

    var illutiaAdfs = LoadIllutiaAdfs(Paths.IllutiaData, out int illutiaSkipped);
    if (illutiaAdfs.Count == 0)
    {
        Console.WriteLine($"Illutia sheets unreadable under {Paths.IllutiaData}; nothing written");
        return;
    }

    var appearance = new List<CompiledSpriteFramesResource>();
    foreach (var ca in new CompiledEnc(Paths.CompiledEnc).CompiledAnimations)
    {
        try
        {
            appearance.Add(CompiledAnimationBuilder.BuildCharacterResource(ca, illutiaAdfs));
        }
        catch
        {
            // Same per-resource tolerance as AnimationBatchConverter.Convert's build loop.
            illutiaSkipped++;
        }
    }
    appearance.AddRange(aspBuild.Resources);

    string resourcesDir = Path.Combine(repoRoot, "Assets", "Resources");
    var heights = AnimationMetadataWriter.MergeHeights(new[]
    {
        AnimationMetadataWriter.LoadHeights(Path.Combine(resourcesDir, "AnimationHeights.txt")),
        target.AnimationHeights,
    });
    var firstFrames = AnimationMetadataWriter.MergeFirstFrames(new[]
    {
        AnimationMetadataWriter.LoadFirstFrames(Path.Combine(resourcesDir, "AnimationToFirstFrame.txt")),
        target.AnimationToFirstFrame,
    });

    var outputs = new List<(string Path, string Content)>
    {
        (Path.Combine(repoRoot, target.RelativeOutputPath),
            SpriteFramesWriter.Build(target.Animations)),
        (Path.Combine(resourcesDir, "AnimationHeights.txt"),
            AnimationMetadataWriter.BuildHeightsText(heights)),
        (Path.Combine(resourcesDir, "AnimationToFirstFrame.txt"),
            AnimationMetadataWriter.BuildFirstFrameText(firstFrames)),
        (Path.Combine(repoRoot, "Assets", "Sprites", "appearance-manifest.json"),
            AppearanceManifestBuilder.Build(appearance)),
        (Path.Combine(repoRoot, "Assets", "Sprites", ManifestFileStore.AnimationFileName),
            AnimationManifestBuilder.BuildCombined(
                Paths.IllutiaData, Paths.CompiledEnc, Paths.AsperetaData, Paths.AsperetaCompiledEnc,
                Paths.ItemTileSheets)),
    };

    int written = 0;
    foreach (var (path, content) in outputs)
        written += PublishIfChanged(path, content);

    Console.WriteLine($"Aspereta body {bodyId} -> output body {outputBodyId}: {written} file(s) written, {aspBuild.Resources.Count} Aspereta resources total, {illutiaSkipped} illutia resources skipped");
    foreach (var e in aspBuild.Diagnostics) Console.WriteLine($"  ASPERETA {e}");
    return;
}

if (args.Length >= 1 && args[0] == "tiles")
{
    string repoRoot = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("..", ".."));

    string assetDir = Path.Combine(repoRoot, "Assets", "Sprites");
    var sheets = TileSheetGenerator.Generate(Paths.IllutiaData, Paths.SpriteBundleConfig, Path.Combine(repoRoot, "Assets", "Maps"));
    TileSheetGenerator.Write(assetDir, sheets);
    Console.WriteLine($"Wrote {sheets.Count} tile sheets -> {Path.Combine(assetDir, TileSheetGenerator.OutputFileName)}");
    return;
}

Console.WriteLine("Usage: AssetConverter batch [outDir] | frames <id> | animations [repoRoot] | maps [outDir] | manifest [outPath] | aspereta-mapping [outPath] | aspereta [repoRoot] | aspereta-body <id> [repoRoot] | tiles [repoRoot] | all [repoRoot]");
