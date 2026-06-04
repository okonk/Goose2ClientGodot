using Goose2.AssetConverter;

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

Console.WriteLine("Usage: AssetConverter batch [outDir] | frames <id>");
