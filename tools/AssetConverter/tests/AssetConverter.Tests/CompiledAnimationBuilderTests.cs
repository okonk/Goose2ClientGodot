using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;
using Xunit;

namespace AssetConverter.Tests;

public class CompiledAnimationBuilderTests
{
    [Fact]
    public void BuildCharacterResource_Body1_EmitsWalkIdleAliasesAndMetadata()
    {
        var compiled = new CompiledEnc(Paths.CompiledEnc);
        var body1 = compiled.CompiledAnimations[0];
        var adfs = new[] { 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125 }
            .ToDictionary(id => id, id => new AdfFile(Paths.Adf(id)));

        var result = CompiledAnimationBuilder.BuildCharacterResource(body1, adfs);

        Assert.Equal(AnimationType.Body, result.Type);
        Assert.Equal(1, result.Id);
        Assert.Equal("Assets/Sprites/Bodies/1/animations.tres", result.RelativeOutputPath);
        Assert.Contains(result.Animations, a => a.Name == "walk-no-equip-left" && a.Frames.Count == 5);
        Assert.Contains(result.Animations, a => a.Name == "walk-left" && a.Frames.Count == 5);
        Assert.Contains(result.Animations, a => a.Name == "idle-no-equip-down" && a.Frames.Count == 1);
        Assert.Contains(result.Animations, a => a.Name == "idle-down" && a.Frames.Count == 1);

        var first = Assert.Single(result.AnimationToFirstFrame);
        Assert.Equal("Body-1", first.Key);
        Assert.Equal(new AnimationFrameInfo(115, 3205, 24, 48), first.Value);

        Assert.Equal(48, result.AnimationHeights["Body-1-walk-no-equip-left"]);
        Assert.Equal(48, result.AnimationHeights["Body-1-idle-no-equip-left"]);
        Assert.DoesNotContain(result.AnimationHeights, kvp => kvp.Value == 64);
    }

    [Fact]
    public void BuildCharacterResource_MissingSheetRecordsWarningAndContinues()
    {
        var compiled = new CompiledEnc(Paths.CompiledEnc);
        var body1 = compiled.CompiledAnimations[0];
        var adfs = new Dictionary<int, AdfFile> { [115] = new(Paths.Adf(115)) };

        var result = CompiledAnimationBuilder.BuildCharacterResource(body1, adfs);

        Assert.NotEmpty(result.Animations);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("116"));
    }
}
