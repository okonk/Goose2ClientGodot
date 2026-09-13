using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class TerrainCatalogTests
{
    private static readonly Guid Id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Id2 = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
    private static readonly Guid Id3 = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Fact]
    public void Pattern_Get_ReturnsAllNinePeers()
    {
        var pattern = new TerrainPattern(
            Center: Id1,
            North: Id2,
            East: Id3,
            South: Guid.NewGuid(),
            West: Guid.NewGuid(),
            NorthEast: Guid.NewGuid(),
            SouthEast: Guid.NewGuid(),
            SouthWest: Guid.NewGuid(),
            NorthWest: Guid.NewGuid());

        Assert.Equal(Id1, pattern.Get(TerrainPeer.Center));
        Assert.Equal(Id2, pattern.Get(TerrainPeer.North));
        Assert.Equal(Id3, pattern.Get(TerrainPeer.East));
        Assert.Equal(pattern.South, pattern.Get(TerrainPeer.South));
        Assert.Equal(pattern.West, pattern.Get(TerrainPeer.West));
        Assert.Equal(pattern.NorthEast, pattern.Get(TerrainPeer.NorthEast));
        Assert.Equal(pattern.SouthEast, pattern.Get(TerrainPeer.SouthEast));
        Assert.Equal(pattern.SouthWest, pattern.Get(TerrainPeer.SouthWest));
        Assert.Equal(pattern.NorthWest, pattern.Get(TerrainPeer.NorthWest));
    }

    [Fact]
    public void Pattern_AllowsEmptyPeersAndDefaultsToAllEmpty()
    {
        var pattern = new TerrainPattern();

        Assert.All(
            Enum.GetValues<TerrainPeer>(),
            peer => Assert.Null(pattern.Get(peer)));
    }

    [Fact]
    public void Constructor_DefensivelyCopiesNestedCollections()
    {
        var terrainList = new List<TerrainDefinition>
        {
            new(Id1, "Grass", null),
            new(Id2, "Water", null)
        };
        var graphicList = new List<TerrainGraphicDefinition>
        {
            new(new TerrainGraphicReference(0, 1), new TerrainPattern(Center: Id1)),
            new(new TerrainGraphicReference(0, 2), new TerrainPattern(Center: Id2))
        };

        var catalog = new TerrainCatalog(terrainList, graphicList);

        terrainList.Add(new TerrainDefinition(Id3, "Sand", null));
        terrainList[0] = new TerrainDefinition(Id3, "Replaced", null);
        terrainList.RemoveAt(1);
        graphicList.Clear();
        graphicList.Add(new TerrainGraphicDefinition(new TerrainGraphicReference(9, 9), new TerrainPattern()));

        var terrainCount = catalog.Terrains.Count;
        Assert.Equal(2, terrainCount);
        Assert.Equal(Id1, catalog.Terrains[0].Id);
        Assert.Equal("Grass", catalog.Terrains[0].Name);
        Assert.Equal(Id2, catalog.Terrains[1].Id);
        var graphicCount = catalog.Graphics.Count;
        Assert.Equal(2, graphicCount);
        Assert.Equal(new TerrainGraphicReference(0, 1), catalog.Graphics[0].Reference);
        Assert.Equal(Id1, catalog.Graphics[0].Pattern.Center);
        Assert.Equal(new TerrainGraphicReference(0, 2), catalog.Graphics[1].Reference);
        Assert.Equal(Id2, catalog.Graphics[1].Pattern.Center);

        Assert.NotSame(terrainList, catalog.Terrains);
        Assert.NotSame(graphicList, catalog.Graphics);
    }

    [Fact]
    public void Catalog_ExposesReadOnlyViews()
    {
        var catalog = new TerrainCatalog(
            new[] { new TerrainDefinition(Id1, "Grass", null) },
            new[] { new TerrainGraphicDefinition(new TerrainGraphicReference(0, 1), new TerrainPattern()) });

        Assert.True(catalog.Terrains is IReadOnlyList<TerrainDefinition>);
        Assert.True(catalog.Graphics is IReadOnlyList<TerrainGraphicDefinition>);
        Assert.Throws<NotSupportedException>(() => ((IList<TerrainDefinition>)catalog.Terrains)[0] = null!);
        Assert.Throws<NotSupportedException>(() => ((IList<TerrainGraphicDefinition>)catalog.Graphics).Add(null!));
    }

    [Fact]
    public void Catalog_EqualityIsValueBasedOverBothCollections()
    {
        var a = new TerrainCatalog(
            new List<TerrainDefinition> { new(Id1, "Grass", null) },
            new List<TerrainGraphicDefinition> { new(new TerrainGraphicReference(0, 1), new TerrainPattern(Center: Id1)) });
        var b = new TerrainCatalog(
            new List<TerrainDefinition> { new(Id1, "Grass", null) },
            new List<TerrainGraphicDefinition> { new(new TerrainGraphicReference(0, 1), new TerrainPattern(Center: Id1)) });
        var c = new TerrainCatalog(
            new List<TerrainDefinition> { new(Id1, "grass", null) },
            new List<TerrainGraphicDefinition> { new(new TerrainGraphicReference(0, 1), new TerrainPattern(Center: Id1)) });

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Records_EqualityIsStructural()
    {
        Assert.Equal(new TerrainGraphicReference(3, 7), new TerrainGraphicReference(3, 7));
        Assert.NotEqual(new TerrainGraphicReference(3, 7), new TerrainGraphicReference(3, 8));
        Assert.Equal(new TerrainColor(1, 2, 3), new TerrainColor(1, 2, 3));
        Assert.Equal(new TerrainDefinition(Id1, "Grass", null), new TerrainDefinition(Id1, "Grass", null));
        Assert.NotEqual(new TerrainDefinition(Id1, "Grass", null), new TerrainDefinition(Id1, "grass", null));
        Assert.Equal(new TerrainPattern(Center: Id1), new TerrainPattern(Center: Id1));
        Assert.NotEqual(new TerrainPattern(Center: Id1), new TerrainPattern(North: Id1));
        Assert.Equal(
            new TerrainGraphicDefinition(new TerrainGraphicReference(0, 1), new TerrainPattern(Center: Id1)),
            new TerrainGraphicDefinition(new TerrainGraphicReference(0, 1), new TerrainPattern(Center: Id1)));
    }

    [Fact]
    public void DeriveColor_UsesStableIdGoldenVectors()
    {
        Assert.Equal(new TerrainColor(0xE6, 0x50, 0xBD), TerrainColor.Derive(Id1));
        Assert.Equal(new TerrainColor(0x9F, 0xE6, 0x50), TerrainColor.Derive(Id2));
        Assert.Equal(new TerrainColor(0xC1, 0xE6, 0x50), TerrainColor.Derive(Id3));
    }

    [Fact]
    public void DeriveColor_IsStableAcrossCallsAndGuidCasing()
    {
        var upper = new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
        Assert.Equal(TerrainColor.Derive(Id3), TerrainColor.Derive(upper));
        Assert.Equal(TerrainColor.Derive(Id1), TerrainColor.Derive(Id1));
    }

    [Fact]
    public void DisplayColor_FallsBackToDerivedColorWithoutOverride()
    {
        var definition = new TerrainDefinition(Id1, "Grass", null);

        Assert.Equal(TerrainColor.Derive(Id1), definition.DisplayColor);
    }

    [Fact]
    public void DisplayColor_UsesOverrideWhenPresent()
    {
        var overrideColor = new TerrainColor(0x11, 0x22, 0x33);
        var definition = new TerrainDefinition(Id1, "Grass", overrideColor);

        Assert.Equal(overrideColor, definition.DisplayColor);
        Assert.NotEqual(TerrainColor.Derive(Id1), definition.DisplayColor);
    }

    [Fact]
    public void DisplayColor_DoesNotChangeWhenNameChanges()
    {
        var before = new TerrainDefinition(Id2, "Mud", null);
        var after = new TerrainDefinition(Id2, "Freshly Renamed Mud", null);

        Assert.Equal(before.DisplayColor, after.DisplayColor);
        Assert.NotEqual("Mud", after.Name);
    }
}
