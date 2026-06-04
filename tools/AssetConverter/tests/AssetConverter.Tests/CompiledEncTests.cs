using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Xunit;

namespace AssetConverter.Tests;

public class CompiledEncTests
{
    [Fact]
    public void CompiledEnc_HasExpectedRecordCountAndFirstBodyRecord()
    {
        var compiled = new CompiledEnc(Paths.CompiledEnc);

        Assert.Equal(841, compiled.CompiledAnimations.Count);
        var body1 = compiled.CompiledAnimations[0];
        Assert.Equal(AnimationType.Body, body1.Type);
        Assert.Equal(1, body1.Id);
        Assert.Equal(new[] { 115,116,117,118,119,120,121,122,123,124,125 }, body1.AnimationFiles);
        Assert.Equal(new[] { 3220, 3221, 3222, 3223 }, new[]
        {
            body1.AnimationIndexes[(int)AnimationDirection.Left * 11 + (int)AnimationOrder.WalkingNoEquip],
            body1.AnimationIndexes[(int)AnimationDirection.Down * 11 + (int)AnimationOrder.WalkingNoEquip],
            body1.AnimationIndexes[(int)AnimationDirection.Right * 11 + (int)AnimationOrder.WalkingNoEquip],
            body1.AnimationIndexes[(int)AnimationDirection.Up * 11 + (int)AnimationOrder.WalkingNoEquip],
        });
    }

    [Fact]
    public void CompiledEnc_SheetToAnimationIndexesAllNonZeroFiles()
    {
        var compiled = new CompiledEnc(Paths.CompiledEnc);

        Assert.True(compiled.SheetToAnimation.TryGetValue(115, out var body1));
        Assert.Equal(AnimationType.Body, body1.Type);
        Assert.Equal(1, body1.Id);
    }
}
