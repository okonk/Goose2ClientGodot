using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;
using AssetConverter.Tests.Fixtures;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaCompiledEncTests
{
    private static int[] DistinctIndexes()
    {
        var indexes = new int[32];
        for (int i = 0; i < 32; i++) indexes[i] = 1000 + i;
        return indexes;
    }

    private static string WriteSingleEntry(int rawType, int[] indexes)
    {
        var root = AnimationSourceFixture.CreateDirectory();
        var path = AnimationSourceFixture.AsperetaCompiledEncPath(root);
        AnimationSourceFixture.WriteAsperetaCompiledEncRaw(path, (rawType, 42, indexes));
        return path;
    }

    [Theory]
    [InlineData(1, AnimationType.Body)]
    [InlineData(2, AnimationType.Hair)]
    [InlineData(3, AnimationType.Hand)]
    [InlineData(4, AnimationType.Chest)]
    [InlineData(5, AnimationType.Helm)]
    [InlineData(6, AnimationType.Legs)]
    [InlineData(7, AnimationType.Feet)]
    public void RawType_MapsToExpectedAnimationType(int rawType, AnimationType expected)
    {
        var entry = AsperetaCompiledEnc.Load(WriteSingleEntry(rawType, DistinctIndexes()))[0];
        Assert.Equal(expected, entry.Type);
    }

    [Fact]
    public void RawType3_IsHand_NeverEyes()
    {
        var entry = AsperetaCompiledEnc.Load(WriteSingleEntry(3, DistinctIndexes()))[0];
        Assert.Equal(AnimationType.Hand, entry.Type);
        Assert.NotEqual(AnimationType.Eyes, entry.Type);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(99)]
    public void UnknownRawType_ThrowsInvalidDataException(int rawType)
    {
        var path = WriteSingleEntry(rawType, DistinctIndexes());
        var ex = Assert.Throws<InvalidDataException>(() => AsperetaCompiledEnc.Load(path));
        Assert.Contains(rawType.ToString(), ex.Message);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(0, 3)]
    [InlineData(0, 4)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(1, 4)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(2, 4)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    [InlineData(3, 4)]
    public void Walk_ReachesAllStatesForAllFacings(int facing, int state)
    {
        var indexes = DistinctIndexes();
        var entry = AsperetaCompiledEnc.Load(WriteSingleEntry(1, indexes))[0];
        Assert.Equal(indexes[facing * 4 + state - 1], entry.Walk(facing, state));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(0, 3)]
    [InlineData(0, 4)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(1, 4)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(2, 4)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    [InlineData(3, 4)]
    public void Attack_ReachesAllStatesForAllFacings(int facing, int state)
    {
        var indexes = DistinctIndexes();
        var entry = AsperetaCompiledEnc.Load(WriteSingleEntry(1, indexes))[0];
        Assert.Equal(indexes[16 + facing * 4 + state - 1], entry.Attack(facing, state));
    }

    [Fact]
    public void SingleStateAccessors_MatchStateOne()
    {
        var indexes = DistinctIndexes();
        var entry = AsperetaCompiledEnc.Load(WriteSingleEntry(1, indexes))[0];
        for (int facing = 0; facing < 4; facing++)
        {
            Assert.Equal(entry.Walk(facing, 1), entry.Walk(facing));
            Assert.Equal(entry.Attack(facing, 1), entry.Attack(facing));
        }
    }

    [Theory]
    [InlineData(-1, 1, false)]
    [InlineData(4, 1, false)]
    [InlineData(0, 0, true)]
    [InlineData(0, 5, true)]
    public void Accessors_ValidateFacingAndState(int facing, int state, bool isAttack)
    {
        var entry = AsperetaCompiledEnc.Load(WriteSingleEntry(1, DistinctIndexes()))[0];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            isAttack ? entry.Attack(facing, state) : entry.Walk(facing, state));
    }
}
