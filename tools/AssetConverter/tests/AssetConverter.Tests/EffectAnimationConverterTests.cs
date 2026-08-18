using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;
using Xunit;

namespace AssetConverter.Tests;

public class EffectAnimationConverterTests
{
    /// <summary>First animation on a sheet no compiled animation claims — an effect sheet.</summary>
    private static (int sheet, int animationId) FindFirstEffectSheetAnimation()
    {
        var compiled = new CompiledEnc(Paths.CompiledEnc);

        foreach (var file in Directory.EnumerateFiles(Paths.IllutiaData, "*.adf").OrderBy(p => p))
        {
            var adf = new AdfFile(file);
            if (adf.Type != AdfType.Graphic || adf.AnimationCount == 0 || adf.Animations is null)
                continue;
            if (compiled.SheetToAnimation.ContainsKey(adf.FileNumber))
                continue;

            foreach (var id in adf.Animations.Keys.OrderBy(id => id))
                return (adf.FileNumber, id);
        }

        throw new InvalidOperationException("No effect sheet fixture found");
    }

    private static string EffectPath(string outRoot, int effectId) =>
        Path.Combine(outRoot, $"Assets/Sprites/Effects/{effectId}/animations.tres");

    [Fact]
    public void Convert_WritesEffectSheetAnimationsAndSkipsCompiledIds()
    {
        var (sheet, effectId) = FindFirstEffectSheetAnimation();
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
        var (sheet, effectId) = FindFirstEffectSheetAnimation();
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
        var (sheet, effectId) = FindFirstEffectSheetAnimation();
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
        var (sheet, effectId) = FindFirstEffectSheetAnimation();
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

    /// <summary>
    /// Spell animation 267653 is defined twice: the 9-frame 96x96 effect on sheet 2903 and a
    /// 5-frame 32x32 Body def on sheet 2177. compiled.enc references the id, so an id-level
    /// filter dropped the spell entirely; the sheet rule must emit it from 2903.
    /// </summary>
    [Fact]
    public void Convert_PicksEffectSheetDefinition_ForIdAlsoUsedByCompiledAnimation()
    {
        var outRoot = Path.Combine(Path.GetTempPath(), "ac_effect_dup_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = AnimationBatchConverter.Convert(
                Paths.IllutiaData,
                Paths.CompiledEnc,
                outRoot,
                only: _ => false,
                includeEffects: true,
                onlyEffectsFromSheets: new[] { 2903, 2177 });

            Assert.Equal(1, result.EffectsWritten);

            var tres = File.ReadAllText(EffectPath(outRoot, 267653));
            Assert.Contains("res://Assets/Sprites/sheets/2903.png", tres);
            Assert.DoesNotContain("2177.png", tres);
            Assert.Equal(9, tres.Split("[sub_resource type=\"AtlasTexture\"").Length - 1);
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        }
    }

    /// <summary>
    /// Sheet 6324 belongs to compiled Chest-108 but also carries an unreferenced 48x64 def
    /// (408048). Those equipment leftovers are not effects and must not be emitted.
    /// </summary>
    [Fact]
    public void Convert_SkipsLeftoverAnimationsOnCharacterSheets()
    {
        var outRoot = Path.Combine(Path.GetTempPath(), "ac_effect_leftover_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = AnimationBatchConverter.Convert(
                Paths.IllutiaData,
                Paths.CompiledEnc,
                outRoot,
                only: _ => false,
                includeEffects: true,
                onlyEffectsFromSheets: new[] { 6324 });

            Assert.Equal(0, result.EffectsWritten);
            Assert.False(File.Exists(EffectPath(outRoot, 408048)));
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        }
    }

    /// <summary>
    /// Spell animation 267593 exists only on sheet 2174, which compiled Body-110 claims, so the
    /// sheet rule alone cannot reach it — it is force-included.
    /// </summary>
    [Fact]
    public void Convert_ForceIncludesSharedEffectAnimationsOnCharacterSheets()
    {
        var outRoot = Path.Combine(Path.GetTempPath(), "ac_effect_forced_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = AnimationBatchConverter.Convert(
                Paths.IllutiaData,
                Paths.CompiledEnc,
                outRoot,
                only: _ => false,
                includeEffects: true,
                onlyEffectsFromSheets: new[] { 2174 });

            Assert.Equal(1, result.EffectsWritten);

            var tres = File.ReadAllText(EffectPath(outRoot, 267593));
            Assert.Contains("res://Assets/Sprites/sheets/2174.png", tres);
            Assert.Contains("\"name\": &\"267593\"", tres);
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        }
    }
}
