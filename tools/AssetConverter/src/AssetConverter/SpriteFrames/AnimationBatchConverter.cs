using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.SpriteFrames;

/// <summary>Result of a batch animation conversion run.</summary>
public sealed record AnimationBatchResult(
    int ResourcesWritten,
    int EffectsWritten,
    int Failed,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Failures);

/// <summary>
/// Batch-converts all compiled animations from <c>compiled.enc</c> into
/// Godot SpriteFrames <c>.tres</c> resources and merged metadata files.
/// </summary>
public static class AnimationBatchConverter
{
    /// <summary>
    /// Loads all ADFs, parses <paramref name="compiledEncPath"/>, builds a resource for each
    /// compiled animation that passes <paramref name="only"/>, writes <c>.tres</c> files and
    /// merged metadata under <paramref name="outRoot"/>.
    /// </summary>
    /// <param name="dataDir">Directory containing the <c>*.adf</c> files.</param>
    /// <param name="compiledEncPath">Path to <c>compiled.enc</c>.</param>
    /// <param name="outRoot">Root directory under which resources and metadata are written.</param>
    /// <param name="only">Optional predicate to select which compiled animations to process.</param>
    /// <param name="includeEffects">When true, also convert uncompiled ADF animations as effect resources.</param>
    /// <param name="onlyEffectsFromSheets">Optional filter: only process effects from these sheet file numbers.</param>
    /// <returns>Result with counts of resources written, failures, warnings, and failure details.</returns>
    public static AnimationBatchResult Convert(
        string dataDir,
        string compiledEncPath,
        string outRoot,
        Func<CompiledAnimation, bool>? only = null,
        bool includeEffects = false,
        int[]? onlyEffectsFromSheets = null,
        IReadOnlyList<CompiledSpriteFramesResource>? extraResources = null)
    {
        var warnings = new List<string>();
        var failures = new List<string>();
        int resourcesWritten = 0;
        int effectsWritten = 0;
        int failed = 0;

        // 1. Load all ADFs into a dictionary by FileNumber
        var adfs = new Dictionary<int, AdfFile>();
        foreach (var file in Directory.EnumerateFiles(dataDir, "*.adf"))
        {
            try
            {
                var adf = new AdfFile(file);
                adfs[adf.FileNumber] = adf;
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to load {Path.GetFileName(file)}: {ex.GetType().Name} {ex.Message}");
            }
        }

        // 2. Parse compiled.enc
        CompiledEnc compiled;
        try
        {
            compiled = new CompiledEnc(compiledEncPath);
        }
        catch (Exception ex)
        {
            return new AnimationBatchResult(0, 0, 1, warnings, new[] { $"Failed to parse compiled.enc: {ex.Message}" });
        }

        // 2b. Build set of all compiled animation ids (non-zero) to avoid duplicating as effects
        var compiledAnimationIds = compiled.CompiledAnimations
            .SelectMany(c => c.AnimationIndexes)
            .Where(id => id != 0)
            .ToHashSet();

        // 3. For each compiled animation, build a resource
        var allResources = new List<CompiledSpriteFramesResource>();

        foreach (var ca in compiled.CompiledAnimations)
        {
            if (only != null && !only(ca))
                continue;

            try
            {
                var resource = CompiledAnimationBuilder.BuildCharacterResource(ca, adfs);
                warnings.AddRange(resource.Warnings);
                allResources.Add(resource);
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"{ca.Type}-{ca.Id}: {ex.GetType().Name} {ex.Message}");
            }
        }

        // 4. Serialize and write .tres files
        foreach (var resource in allResources)
        {
            if (resource.Animations.Count == 0)
            {
                failed++;
                failures.Add($"{resource.Type}-{resource.Id}: zero animations, skipping empty .tres");
                continue;
            }

            string fullPath = Path.Combine(outRoot, resource.RelativeOutputPath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                string tres = SpriteFramesWriter.Build(resource.Animations);
                File.WriteAllText(fullPath, tres);
                resourcesWritten++;
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"{resource.Type}-{resource.Id}: failed to write {resource.RelativeOutputPath}: {ex.Message}");
            }
        }

        // 5. Process effect animations (uncompiled ADF animations not in compiled set)
        var effectHeights = new Dictionary<string, int>();
        if (includeEffects)
        {
            var onlySheets = onlyEffectsFromSheets is not null ? new HashSet<int>(onlyEffectsFromSheets) : null;

            foreach (var adf in adfs.Values)
            {
                if (adf.Type != AdfType.Graphic || adf.Animations is null)
                    continue;

                if (onlySheets is not null && !onlySheets.Contains(adf.FileNumber))
                    continue;

                foreach (var kvp in adf.Animations)
                {
                    int animationId = kvp.Key;
                    Animation animation = kvp.Value;

                    // Skip if this animation id is already compiled
                    if (compiledAnimationIds.Contains(animationId))
                        continue;

                    string effectId = animationId.ToString();
                    string texturePath = $"res://Assets/Sprites/sheets/{adf.FileNumber}.png";
                    string relativePath = $"Assets/Sprites/Effects/{effectId}/animations.tres";
                    string fullPath = Path.Combine(outRoot, relativePath);

                    try
                    {
                        var spec = SpriteFramesAnimationSpec.FromFrames(effectId, adf.FileNumber, texturePath, animation.Frames);
                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                        string tres = SpriteFramesWriter.Build(new[] { spec });
                        File.WriteAllText(fullPath, tres);
                        effectsWritten++;

                        // Record height metadata if max frame height != 64
                        int maxHeight = animation.Frames.Max(f => f.H);
                        if (maxHeight != 64)
                        {
                            effectHeights[effectId] = maxHeight;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add($"Effect {effectId}: failed to write {relativePath}: {ex.Message}");
                    }
                }
            }
        }

        // 5b. Append any extra resources (e.g. Aspereta monsters) before metadata write
        if (extraResources is not null)
            allResources.AddRange(extraResources);

        // 6. Merge and write metadata (only after all resources are built)
        if (allResources.Any(r => r.Animations.Count > 0) || effectHeights.Count > 0)
        {
            try
            {
                WriteMergedMetadata(outRoot, allResources, effectHeights);
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"Metadata merge/write failed: {ex.GetType().Name} {ex.Message}");
            }
        }

        return new AnimationBatchResult(resourcesWritten, effectsWritten, failed, warnings, failures);
    }

    /// <summary>
    /// Merges first-frame and height metadata from character resources and effect heights,
    /// then writes <c>AnimationToFirstFrame.txt</c> and <c>AnimationHeights.txt</c>.
    /// </summary>
    public static void WriteMergedMetadata(
        string outRoot,
        IEnumerable<CompiledSpriteFramesResource> resources,
        IReadOnlyDictionary<string, int> effectHeights)
    {
        var mergedFirstFrames = AnimationMetadataWriter.MergeFirstFrames(
            resources.Select(r => r.AnimationToFirstFrame));
        var characterHeights = AnimationMetadataWriter.MergeHeights(
            resources.Select(r => r.AnimationHeights));
        var mergedHeights = AnimationMetadataWriter.MergeHeights(
            new[] { characterHeights, effectHeights });

        string resourcesDir = Path.Combine(outRoot, "Assets", "Resources");
        AnimationMetadataWriter.Write(resourcesDir, mergedFirstFrames, mergedHeights);
    }
}
