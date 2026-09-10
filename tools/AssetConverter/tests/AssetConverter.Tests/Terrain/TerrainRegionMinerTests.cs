using Goose2.AssetConverter.Terrain;
using MapEditor.Core;
using MapEditor.Core.Terrain;

namespace AssetConverter.Tests.Terrain;

public class TerrainRegionMinerTests
{
    [Fact]
    public void Masks_UseLockedCoordinateDirectionsAndTreatOutsideAsAbsent()
    {
        var document = MapDocument.Create(3, 2);
        document.SetLayer(1, 1, 0, new MapTileLayer(1, 100));
        document.SetLayer(1, 0, 0, new MapTileLayer(1, 101));
        document.SetLayer(2, 1, 0, new MapTileLayer(1, 101));
        document.SetLayer(0, 1, 0, new MapTileLayer(1, 101));
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 102));

        var result = TerrainRegionMiner.Mine(
            new TerrainDecodedMap("test", document),
            new[]
            {
                new TerrainGraphicReference(1, 100),
                new TerrainGraphicReference(1, 101),
            });

        var region = Assert.Single(result.Regions);
        Assert.Equal((1, 0), (region.MinimumX, region.MinimumY));
        Assert.Equal(4, region.Placements.Count);
        var placement = region.Placements.Single(p => p.X == 1 && p.Y == 1);
        Assert.Equal(new TerrainGraphicReference(1, 100), placement.Reference);
        Assert.Equal(TerrainMasks.North | TerrainMasks.East | TerrainMasks.West, placement.Mask);
        Assert.Equal(0.25, placement.Weight);
        Assert.Equal(2, result.DiagonalTrials);
    }

    [Fact]
    public void Masks_NorthEastNeighborsCountOneNorthEastCornerTrial()
    {
        var document = MapDocument.Create(3, 2);
        document.SetLayer(1, 1, 0, new MapTileLayer(1, 100));
        document.SetLayer(1, 0, 0, new MapTileLayer(1, 101));
        document.SetLayer(2, 1, 0, new MapTileLayer(1, 101));

        var result = TerrainRegionMiner.Mine(
            new TerrainDecodedMap("test", document),
            new[]
            {
                new TerrainGraphicReference(1, 100),
                new TerrainGraphicReference(1, 101),
            });

        var region = Assert.Single(result.Regions);
        var placement = region.Placements.Single(p => p.X == 1 && p.Y == 1);
        Assert.Equal(TerrainMasks.North | TerrainMasks.East, placement.Mask);
        Assert.Equal(1, result.DiagonalTrials);
    }

    [Fact]
    public void Masks_SouthWestNeighborsCountOneSouthWestCornerTrial()
    {
        var document = MapDocument.Create(3, 3);
        document.SetLayer(1, 1, 0, new MapTileLayer(1, 100));
        document.SetLayer(0, 1, 0, new MapTileLayer(1, 101));
        document.SetLayer(1, 2, 0, new MapTileLayer(1, 101));

        var result = TerrainRegionMiner.Mine(
            new TerrainDecodedMap("test", document),
            new[]
            {
                new TerrainGraphicReference(1, 100),
                new TerrainGraphicReference(1, 101),
            });

        var region = Assert.Single(result.Regions);
        var placement = region.Placements.Single(p => p.X == 1 && p.Y == 1);
        Assert.Equal(TerrainMasks.South | TerrainMasks.West, placement.Mask);
        Assert.Equal(1, result.DiagonalTrials);
    }

    [Fact]
    public void Mine_DiagonalOnlyNeighborsAreAbsentAndContributeNoCornerTrials()
    {
        var document = MapDocument.Create(2, 2);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 100));
        document.SetLayer(1, 1, 0, new MapTileLayer(1, 101));

        var result = TerrainRegionMiner.Mine(
            new TerrainDecodedMap("test", document),
            new[] { new TerrainGraphicReference(1, 100) });

        var region = Assert.Single(result.Regions);
        var placement = Assert.Single(region.Placements);
        Assert.Equal(0, placement.Mask);
        Assert.Equal(0, result.DiagonalTrials);
    }

    [Fact]
    public void Mine_MultipleRegionsSortByRowMajorMinimumCoordinate()
    {
        var document = MapDocument.Create(3, 3);
        document.SetLayer(2, 0, 0, new MapTileLayer(1, 100));
        document.SetLayer(0, 2, 0, new MapTileLayer(1, 100));
        document.SetLayer(1, 1, 0, new MapTileLayer(1, 100));

        var result = TerrainRegionMiner.Mine(
            new TerrainDecodedMap("test", document),
            new[] { new TerrainGraphicReference(1, 100) });

        Assert.Equal(3, result.Regions.Count);
        Assert.Equal((2, 0), (result.Regions[0].MinimumX, result.Regions[0].MinimumY));
        Assert.Equal((1, 1), (result.Regions[1].MinimumX, result.Regions[1].MinimumY));
        Assert.Equal((0, 2), (result.Regions[2].MinimumX, result.Regions[2].MinimumY));
        Assert.All(result.Regions, region => Assert.Equal(1.0 / 3.0, Assert.Single(region.Placements).Weight));
    }

    [Fact]
    public void Mine_RegionMassIsInverselyProportionalToRegionCountAndPlacements()
    {
        var document = MapDocument.Create(2, 2);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 100));
        document.SetLayer(1, 1, 0, new MapTileLayer(1, 100));

        var result = TerrainRegionMiner.Mine(
            new TerrainDecodedMap("test", document),
            new[] { new TerrainGraphicReference(1, 100) });

        Assert.Equal(2, result.Regions.Count);
        Assert.All(result.Regions, region => Assert.Equal(0.5, Assert.Single(region.Placements).Weight));
    }
}
