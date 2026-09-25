using Goose2.AssetConverter.SpriteFrames;

namespace Goose2.AssetConverter.Aspereta;

public record AsperetaEffectsResult(
    int EffectsWritten, int Failed, List<string> Failures, IReadOnlyList<string> Diagnostics);

public static class AsperetaEffectsConverter
{
    public static AsperetaEffectsResult Convert(AsperetaAnimationCatalog catalog, string outRoot)
    {
        var failures = new List<string>();
        int written = 0, failed = 0;
        var effectHeights = new Dictionary<string, int>();

        foreach (int sourceId in catalog.UnclaimedResolvedIds)
        {
            int effectId = AsperetaSheets.GraphicBase + sourceId;
            string relativePath = $"Assets/Sprites/Effects/{effectId}/animations.tres";
            string fullPath = Path.Combine(outRoot, relativePath);
            var resolution = catalog.Resolved[sourceId];

            try
            {
                var frames = resolution.Frames
                    .Select(f => new SpriteFrameSpec(f.Sheet, TexturePath(f.Sheet), f.Frame))
                    .ToList();
                var spec = new SpriteFramesAnimationSpec(
                    effectId.ToString(), frames, true, resolution.Fps);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, SpriteFramesWriter.Build(new[] { spec }));
                written++;

                int maxHeight = resolution.Frames.Max(f => f.Frame.H);
                if (maxHeight != 64)
                    effectHeights[effectId.ToString()] = maxHeight;
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"Effect {effectId}: failed to write {relativePath}: {ex.Message}");
            }
        }

        if (effectHeights.Count > 0)
        {
            try
            {
                var resourcesDir = Path.Combine(outRoot, "Assets", "Resources");
                // SAFE merge: preserve any existing metadata already under outRoot.
                // AnimationMetadataWriter.Write overwrites both files, so re-load first.
                var existingHeights = AnimationMetadataWriter.LoadHeights(
                    Path.Combine(resourcesDir, "AnimationHeights.txt"));
                var existingFirst = AnimationMetadataWriter.LoadFirstFrames(
                    Path.Combine(resourcesDir, "AnimationToFirstFrame.txt"));
                var mergedHeights = AnimationMetadataWriter.MergeHeights(
                    new[] { existingHeights, effectHeights });
                AnimationMetadataWriter.Write(resourcesDir, existingFirst, mergedHeights);
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"Metadata merge/write failed: {ex.GetType().Name} {ex.Message}");
            }
        }

        return new AsperetaEffectsResult(written, failed, failures, catalog.Diagnostics);
    }

    private static string TexturePath(int sheetNumber) => $"res://Assets/Sprites/sheets/{sheetNumber}.png";
}
