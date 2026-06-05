using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;
using Xunit;

namespace AssetConverter.Tests;

public class EffectAnimationConverterTests
{
    private static (int sheet, int animationId) FindFirstUncompiledAnimation()
    {
        var compiled = new CompiledEnc(Paths.CompiledEnc);
        var compiledIds = compiled.CompiledAnimations
            .SelectMany(c => c.AnimationIndexes)
            .Where(id => id != 0)
            .ToHashSet();

        foreach (var file in Directory.EnumerateFiles(Paths.IllutiaData, "*.adf").OrderBy(p => p))
        {
            var adf = new AdfFile(file);
            if (adf.Type != AdfType.Graphic || adf.Animations is null) continue;
            foreach (var id in adf.Animations.Keys.OrderBy(id => id))
                if (!compiledIds.Contains(id)) return (adf.FileNumber, id);
        }

        throw new InvalidOperationException("No uncompiled animation fixture found");
    }

    [Fact]
    public void Convert_WritesUncompiledEffectAnimationsAndSkipsCompiledIds()
    {
        var (sheet, effectId) = FindFirstUncompiledAnimation();
        var outRoot = Path.Combine(Path.GetTempPath(), "ac_effect_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = AnimationBatchConverter.Convert(
                Paths.IllutiaData,
                Paths.CompiledEnc,
                outRoot,
                includeEffects: true,
                onlyEffectsFromSheets: new[] { sheet });

            Assert.True(result.EffectsWritten >= 1);
            var effectPath = Path.Combine(outRoot, $"Assets/Sprites/Effects/{effectId}/animations.tres");
            Assert.True(File.Exists(effectPath));
            Assert.Contains($"\"name\": &\"{effectId}\"", File.ReadAllText(effectPath));

            // Compiled animation id 3220 (Body-1, first animation index) must NOT appear as effect
            Assert.False(File.Exists(Path.Combine(outRoot, "Assets/Sprites/Effects/3220/animations.tres")));
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        }
    }

    [Fact]
    public void Convert_EffectsDisabled_ByDefault()
    {
        var (sheet, effectId) = FindFirstUncompiledAnimation();
        var outRoot = Path.Combine(Path.GetTempPath(), "ac_effect_default_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = AnimationBatchConverter.Convert(
                Paths.IllutiaData,
                Paths.CompiledEnc,
                outRoot,
                only: ca => ca.Type == AnimationType.Body && ca.Id == 1);

            Assert.Equal(0, result.EffectsWritten);
            var effectPath = Path.Combine(outRoot, $"Assets/Sprites/Effects/{effectId}/animations.tres");
            Assert.False(File.Exists(effectPath));
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        }
    }

    [Fact]
    public void Convert_EffectsIncludeHeightMetadataForNonStandardHeight()
    {
        var (sheet, effectId) = FindFirstUncompiledAnimation();
        var outRoot = Path.Combine(Path.GetTempPath(), "ac_effect_height_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = AnimationBatchConverter.Convert(
                Paths.IllutiaData,
                Paths.CompiledEnc,
                outRoot,
                includeEffects: true,
                onlyEffectsFromSheets: new[] { sheet });

            Assert.True(result.EffectsWritten >= 1);

            // Check that height metadata exists
            var heightsPath = Path.Combine(outRoot, "Assets/Resources/AnimationHeights.txt");
            Assert.True(File.Exists(heightsPath));

            // Load the animation to check its max height
            var adf = new AdfFile(Paths.Adf(sheet));
            if (adf.Animations != null && adf.Animations.TryGetValue(effectId, out var anim))
            {
                int maxHeight = anim.Frames.Max(f => f.H);
                if (maxHeight != 64)
                {
                    var heights = File.ReadAllText(heightsPath);
                    Assert.Contains($"{effectId},{maxHeight}", heights);
                }
            }
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        }
    }

    [Fact]
    public void Convert_EffectsDoNotWriteAnimationToFirstFrame()
    {
        var (sheet, effectId) = FindFirstUncompiledAnimation();
        var outRoot = Path.Combine(Path.GetTempPath(), "ac_effect_ff_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = AnimationBatchConverter.Convert(
                Paths.IllutiaData,
                Paths.CompiledEnc,
                outRoot,
                includeEffects: true,
                onlyEffectsFromSheets: new[] { sheet });

            Assert.True(result.EffectsWritten >= 1);

            var firstFramePath = Path.Combine(outRoot, "Assets/Resources/AnimationToFirstFrame.txt");
            Assert.True(File.Exists(firstFramePath));
            var firstFrame = File.ReadAllText(firstFramePath);

            // No effect animation id should appear as a key in first-frame metadata
            // (effect ids are numeric and shouldn't appear as "Type-Id" keys)
            var lines = firstFrame.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.DoesNotContain(lines, l => l.StartsWith(effectId.ToString() + ","));
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        }
    }
}
