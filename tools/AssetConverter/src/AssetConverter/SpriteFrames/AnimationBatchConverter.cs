using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.SpriteFrames;

/// <summary>Result of a batch animation conversion run.</summary>
public sealed record AnimationBatchResult(
    int ResourcesWritten,
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
    /// <returns>Result with counts of resources written, failures, warnings, and failure details.</returns>
    public static AnimationBatchResult Convert(
        string dataDir,
        string compiledEncPath,
        string outRoot,
        Func<CompiledAnimation, bool>? only = null)
    {
        var warnings = new List<string>();
        var failures = new List<string>();
        int resourcesWritten = 0;
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
            return new AnimationBatchResult(0, 1, warnings, new[] { $"Failed to parse compiled.enc: {ex.Message}" });
        }

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

        // 5. Merge and write metadata (only after all resources are built)
        if (allResources.Any(r => r.Animations.Count > 0))
        {
            try
            {
                var mergedFirstFrames = AnimationMetadataWriter.MergeFirstFrames(
                    allResources.Select(r => r.AnimationToFirstFrame));
                var mergedHeights = AnimationMetadataWriter.MergeHeights(
                    allResources.Select(r => r.AnimationHeights));

                string resourcesDir = Path.Combine(outRoot, "Assets", "Resources");
                AnimationMetadataWriter.Write(resourcesDir, mergedFirstFrames, mergedHeights);
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"Metadata merge/write failed: {ex.GetType().Name} {ex.Message}");
            }
        }

        return new AnimationBatchResult(resourcesWritten, failed, warnings, failures);
    }
}
