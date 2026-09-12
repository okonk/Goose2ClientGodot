namespace Goose2.AssetConverter.Manifest;

public static class ManifestFileStore
{
    public const string AnimationFileName = "animation-manifest.json";

    public static void Write(string frameManifestPath, Func<string> frameManifest, Func<string> animationManifest)
    {
        string frame = frameManifest();
        string animation = animationManifest();

        string dir = Path.GetDirectoryName(Path.GetFullPath(frameManifestPath))!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(frameManifestPath, frame);
        File.WriteAllText(Path.Combine(dir, AnimationFileName), animation);
    }

    public static void WriteCombined(string repoRoot, Func<string> frameManifest, Func<string> animationManifest)
    {
        Write(Path.Combine(repoRoot, "Assets", "Sprites", "manifest.json"), frameManifest, animationManifest);
    }
}
