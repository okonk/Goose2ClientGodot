using Goose2.AssetConverter;
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
            Assert.Contains("walk-no-equip-left,48", heights);
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        }
    }
}
