using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;
using Xunit;

namespace AssetConverter.Tests;

public class AnimationNamingTests
{
    [Theory]
    [InlineData(AnimationOrder.WalkingNoEquip, AnimationDirection.Down, "walk-no-equip-down")]
    [InlineData(AnimationOrder.Attack1Hand, AnimationDirection.Left, "attack-1hand-left")]
    [InlineData(AnimationOrder.SpellCast, AnimationDirection.Up, "cast-up")]
    [InlineData(AnimationOrder.Mounted, AnimationDirection.Right, "mounted-walk-right")]
    public void ClipName_MapsOrdersAndDirections(AnimationOrder order, AnimationDirection direction, string expected)
        => Assert.Equal(expected, AnimationNaming.ClipName(order, direction));

    [Theory]
    [InlineData(AnimationOrder.WalkingNoEquip, "idle-no-equip-left")]
    [InlineData(AnimationOrder.WalkingEquip, "idle-equip-left")]
    [InlineData(AnimationOrder.Mounted, "mounted-idle-left")]
    public void TryIdleName_ReturnsOnlyUnityGeneratedIdleOrders(AnimationOrder order, string expected)
    {
        Assert.True(AnimationNaming.TryIdleName(order, AnimationDirection.Left, out var name));
        Assert.Equal(expected, name);
    }

    [Fact]
    public void TryIdleName_DoesNotGenerateIdleForAttack()
        => Assert.False(AnimationNaming.TryIdleName(AnimationOrder.AttackNoEquip, AnimationDirection.Down, out _));

    [Theory]
    [InlineData(AnimationType.Body, "Bodies")]
    [InlineData(AnimationType.Helm, "Helms")]
    [InlineData(AnimationType.Hand, "Hands")]
    public void TypeFolder_MapsPluralFolders(AnimationType type, string expected)
        => Assert.Equal(expected, AnimationNaming.TypeFolder(type));

    [Fact]
    public void DirectionNames_FollowCompiledEncDirectionOrder()
    {
        Assert.Equal("left", AnimationNaming.DirectionName(AnimationDirection.Left));
        Assert.Equal("down", AnimationNaming.DirectionName(AnimationDirection.Down));
        Assert.Equal("right", AnimationNaming.DirectionName(AnimationDirection.Right));
        Assert.Equal("up", AnimationNaming.DirectionName(AnimationDirection.Up));
    }
}
