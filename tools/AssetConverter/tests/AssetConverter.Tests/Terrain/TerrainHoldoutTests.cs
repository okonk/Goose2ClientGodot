using Goose2.AssetConverter.Terrain;

namespace AssetConverter.Tests.Terrain;

public class TerrainHoldoutTests
{
    [Fact]
    public void Holdout_KnownIdentityBytesProduceLockedDigestPrefixAndAssignment()
    {
        Assert.Equal(0xE8B998403A8895A0UL, TerrainHoldout.DigestPrefix("Assets/Maps/Map1.map"));
        Assert.True(TerrainHoldout.IsHeldOut("Assets/Maps/Map1.map", 2));
        Assert.False(TerrainHoldout.IsHeldOut("Assets/Maps/Map1.map", 3));
        Assert.False(TerrainHoldout.IsHeldOut("Assets/Maps/Map5.map", 2));

        var heldOutName = TerrainFixtureBuilder.FindMapFileName(holdoutModulo: 7, heldOut: true);
        var trainingName = TerrainFixtureBuilder.FindMapFileName(holdoutModulo: 7, heldOut: false);
        Assert.NotEqual(heldOutName, trainingName);
        Assert.True(TerrainHoldout.IsHeldOut("Assets/Maps/" + heldOutName, 7));
        Assert.False(TerrainHoldout.IsHeldOut("Assets/Maps/" + trainingName, 7));
    }
}
