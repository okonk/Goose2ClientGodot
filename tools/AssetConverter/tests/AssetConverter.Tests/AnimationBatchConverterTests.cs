using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;
using Xunit;

namespace AssetConverter.Tests;

public class AnimationBatchConverterTests
{
    [Fact]
    public void Convert_OnlyBody1_WritesResourceAndMetadata()
    {
        var outRoot = Path.Combine(Path.GetTempPath(), "ac_anim_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = AnimationBatchConverter.Convert(
                Paths.IllutiaData,
                Paths.CompiledEnc,
                outRoot,
                only: ca => ca.Type == Goose2.AssetConverter.Adf.AnimationType.Body && ca.Id == 1);

            Assert.Equal(1, result.ResourcesWritten);
            var tresPath = Path.Combine(outRoot, "Assets/Sprites/Bodies/1/animations.tres");
            Assert.True(File.Exists(tresPath));
            var tres = File.ReadAllText(tresPath);
            Assert.Contains("\"name\": &\"walk-no-equip-left\"", tres);
            Assert.Contains("\"name\": &\"walk-left\"", tres);
            Assert.Contains("\"name\": &\"idle-down\"", tres);
            Assert.Contains("path=\"res://Assets/Sprites/sheets/115.png\"", tres);

            var firstFrame = File.ReadAllText(Path.Combine(outRoot, "Assets/Resources/AnimationToFirstFrame.txt"));
            Assert.Contains("Body-1,115,3205,24,48", firstFrame);
            var heights = File.ReadAllText(Path.Combine(outRoot, "Assets/Resources/AnimationHeights.txt"));
            Assert.Contains("Body-1-walk-no-equip-left,48", heights);

            var manifestPath = Path.Combine(outRoot, "Assets/Sprites/appearance-manifest.json");
            Assert.True(File.Exists(manifestPath));
            using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.Equal(1, manifest.RootElement.GetProperty("version").GetInt32());
            var body1 = manifest.RootElement.GetProperty("parts").GetProperty("Body").GetProperty("1");
            Assert.Equal(115, body1.GetProperty("noEquip")[0].GetInt32());
            Assert.Equal(3205, body1.GetProperty("noEquip")[1].GetInt32());
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        }
    }

    [Fact]
    public void Convert_WithExtraResources_MonsterBodyAppearsBesideIllutiaBodyWithoutCountChanges()
    {
        var outRoot = Path.Combine(Path.GetTempPath(), "ac_anim_" + Guid.NewGuid().ToString("N"));
        try
        {
            var monster = new CompiledSpriteFramesResource(
                AnimationType.Body,
                10101,
                AnimationNaming.ResourceRelativePath(AnimationType.Body, 10101),
                new[]
                {
                    SpriteFramesAnimationSpec.FromFrames(
                        "idle-no-equip-down", 20001, "res://Assets/Sprites/sheets/20001.png",
                        new[] { new Frame(700123, 0, 0, 48, 64) }),
                },
                new Dictionary<string, AnimationFrameInfo>(),
                new Dictionary<string, int>(),
                Array.Empty<string>());

            var result = AnimationBatchConverter.Convert(
                Paths.IllutiaData,
                Paths.CompiledEnc,
                outRoot,
                only: ca => ca.Type == AnimationType.Body && ca.Id == 1,
                extraResources: new[] { monster });

            Assert.Equal(1, result.ResourcesWritten);
            Assert.Equal(0, result.Failed);

            var manifestPath = Path.Combine(outRoot, "Assets/Sprites/appearance-manifest.json");
            using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
            var bodies = manifest.RootElement.GetProperty("parts").GetProperty("Body");
            Assert.Equal(115, bodies.GetProperty("1").GetProperty("noEquip")[0].GetInt32());
            Assert.Equal(20001, bodies.GetProperty("10101").GetProperty("noEquip")[0].GetInt32());
            Assert.Equal(700123, bodies.GetProperty("10101").GetProperty("noEquip")[1].GetInt32());
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        }
    }

    [Fact]
    public void Convert_MultipleBodies_ScopedHeightKeysPreventMergeConflict()
    {
        var outRoot = Path.Combine(Path.GetTempPath(), "ac_anim_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = AnimationBatchConverter.Convert(
                Paths.IllutiaData,
                Paths.CompiledEnc,
                outRoot,
                only: ca => ca.Type == AnimationType.Body && (ca.Id == 1 || ca.Id == 2));

            // No metadata merge conflict failure
            Assert.DoesNotContain(result.Failures, f => f.Contains("Conflicting values"));

            var heightsPath = Path.Combine(outRoot, "Assets/Resources/AnimationHeights.txt");
            Assert.True(File.Exists(heightsPath), "AnimationHeights.txt should exist");
            var heights = File.ReadAllText(heightsPath);

            // Body-1 has scoped height keys
            Assert.Contains("Body-1-walk-no-equip-left,48", heights);
            Assert.Contains("Body-1-idle-no-equip-left,48", heights);

            // Keys are scoped: no bare animation names as standalone lines
            var lines = heights.Split('\n');
            Assert.DoesNotContain(lines, l => l == "walk-no-equip-left,48");
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        }
    }
}
