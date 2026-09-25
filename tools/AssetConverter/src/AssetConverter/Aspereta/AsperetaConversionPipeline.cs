using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Manifest;
using Goose2.AssetConverter.SpriteFrames;

namespace Goose2.AssetConverter.Aspereta;

public sealed record AsperetaConversionResult(
    AsperetaAnimationCatalog Catalog,
    IReadOnlyList<CompiledSpriteFramesResource> Resources,
    int ResourcesWritten,
    int EffectsWritten,
    int Failed,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Diagnostics);

public static class AsperetaConversionPipeline
{
    public static AsperetaConversionResult Convert(string dataDir, string compiledPath, string outRoot)
    {
        var catalog = AsperetaAnimationCatalog.Load(dataDir, compiledPath);
        var build = AsperetaAnimationResourceBuilder.Build(catalog);

        var failures = new List<string>();
        int resourcesWritten = 0;
        int failed = 0;

        foreach (var resource in build.Resources)
        {
            if (resource.Animations.Count == 0)
            {
                failed++;
                failures.Add($"{resource.Type}-{resource.Id}: zero animations, skipping empty .tres");
                continue;
            }

            try
            {
                string fullPath = Path.Combine(outRoot, resource.RelativeOutputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, SpriteFramesWriter.Build(resource.Animations));
                resourcesWritten++;
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"{resource.Type}-{resource.Id}: failed to write {resource.RelativeOutputPath}: {ex.Message}");
            }
        }

        var effects = AsperetaEffectsConverter.Convert(catalog, outRoot);
        failures.AddRange(effects.Failures);
        failed += effects.Failed;

        try
        {
            MergeResourceMetadata(outRoot, build.Resources);
        }
        catch (Exception ex)
        {
            failed++;
            failures.Add($"Metadata merge/write failed: {ex.GetType().Name} {ex.Message}");
        }

        return new AsperetaConversionResult(catalog, build.Resources, resourcesWritten,
            effects.EffectsWritten, failed, failures, build.Diagnostics);
    }

    public static string BuildCombinedAppearanceManifest(
        string illutiaDataDir, string illutiaCompiledEncPath,
        IReadOnlyList<CompiledSpriteFramesResource> asperetaResources)
    {
        var adfs = new Dictionary<int, AdfFile>();
        foreach (var file in Directory.EnumerateFiles(illutiaDataDir, "*.adf"))
        {
            try
            {
                var adf = new AdfFile(file);
                adfs[adf.FileNumber] = adf;
            }
            catch { }
        }

        var appearance = new List<CompiledSpriteFramesResource>();
        foreach (var ca in new CompiledEnc(illutiaCompiledEncPath).CompiledAnimations)
        {
            try
            {
                appearance.Add(CompiledAnimationBuilder.BuildCharacterResource(ca, adfs));
            }
            catch
            {
                // Same per-resource tolerance as AnimationBatchConverter.Convert's build loop.
            }
        }
        appearance.AddRange(asperetaResources);
        return AppearanceManifestBuilder.Build(appearance);
    }

    private static void MergeResourceMetadata(string outRoot, IReadOnlyList<CompiledSpriteFramesResource> resources)
    {
        var active = resources.Where(r => r.Animations.Count > 0).ToList();
        if (active.Count == 0)
            return;

        string resourcesDir = Path.Combine(outRoot, "Assets", "Resources");
        var existingHeights = AnimationMetadataWriter.LoadHeights(
            Path.Combine(resourcesDir, "AnimationHeights.txt"));
        var existingFirstFrames = AnimationMetadataWriter.LoadFirstFrames(
            Path.Combine(resourcesDir, "AnimationToFirstFrame.txt"));
        var mergedHeights = AnimationMetadataWriter.MergeHeights(new[]
        {
            existingHeights,
            AnimationMetadataWriter.MergeHeights(active.Select(r => r.AnimationHeights)),
        });
        var mergedFirstFrames = AnimationMetadataWriter.MergeFirstFrames(new[]
        {
            existingFirstFrames,
            AnimationMetadataWriter.MergeFirstFrames(active.Select(r => r.AnimationToFirstFrame)),
        });
        AnimationMetadataWriter.Write(resourcesDir, mergedFirstFrames, mergedHeights);
    }
}
