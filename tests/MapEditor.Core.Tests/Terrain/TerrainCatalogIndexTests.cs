using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class TerrainCatalogIndexTests
{
    [Fact]
    public void Index_TryGetTerrain_ReturnsKnownAndRejectsUnknown()
    {
        var index = TerrainCatalogValidator.Validate(TerrainCatalogFixture.Valid()).Index!;

        Assert.True(index.TryGetTerrain(TerrainCatalogFixture.Grass, out var grass));
        Assert.Equal("Grass", grass.Name);
        Assert.False(index.TryGetTerrain(Guid.NewGuid(), out _));
    }

    [Fact]
    public void Index_TryGetGraphic_ReturnsKnownAndRejectsUnknown()
    {
        var index = TerrainCatalogValidator.Validate(TerrainCatalogFixture.Valid()).Index!;

        Assert.True(index.TryGetGraphic(TerrainCatalogFixture.Ref(0, 1), out var graphic));
        Assert.Equal(TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass), graphic.Pattern);
        Assert.False(index.TryGetGraphic(TerrainCatalogFixture.Ref(9, 9), out _));
    }

    [Fact]
    public void Index_GetDisplayColor_UsesOverrideAndDerivedFallback()
    {
        var overrideColor = new TerrainColor(0x11, 0x22, 0x33);
        var catalog = new TerrainCatalog(
            new[]
            {
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass", overrideColor),
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Water, "Water")
            },
            new[]
            {
                TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 2, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Water))
            });

        var index = TerrainCatalogValidator.Validate(catalog).Index!;

        Assert.Equal(overrideColor, index.GetDisplayColor(TerrainCatalogFixture.Grass));
        Assert.Equal(TerrainColor.Derive(TerrainCatalogFixture.Water), index.GetDisplayColor(TerrainCatalogFixture.Water));
        Assert.Throws<KeyNotFoundException>(() => index.GetDisplayColor(Guid.NewGuid()));
    }

    [Fact]
    public void Index_GroupsDuplicatePatternsBeforeVariants()
    {
        var catalog = new TerrainCatalog(
            new[]
            {
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass"),
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Water, "Water")
            },
            new[]
            {
                TerrainCatalogFixture.Graphic(0, 3, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 4, TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Grass, (TerrainPeer.North, TerrainCatalogFixture.Water))),
                TerrainCatalogFixture.Graphic(1, 2, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 2, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(1, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Water))
            });

        var index = TerrainCatalogValidator.Validate(catalog).Index!;
        var candidates = index.GetCandidates(TerrainCatalogFixture.Grass);

        Assert.Equal(3, candidates.Count);
        Assert.Equal(TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Grass), candidates[0].Pattern);
        Assert.Equal(TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass), candidates[1].Pattern);
        Assert.Equal(
            TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Grass, (TerrainPeer.North, TerrainCatalogFixture.Water)),
            candidates[2].Pattern);
        Assert.Equal(
            new[]
            {
                TerrainCatalogFixture.Ref(0, 2),
                TerrainCatalogFixture.Ref(0, 3),
                TerrainCatalogFixture.Ref(1, 2)
            },
            candidates[1].Variants.Select(variant => variant.Reference).ToArray());
    }

    [Fact]
    public void Index_Representatives_PreferMostSameCenterPeersThenReference()
    {
        var catalog = new TerrainCatalog(
            new[] { TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass") },
            new[]
            {
                TerrainCatalogFixture.Graphic(0, 9, TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(1, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass))
            });

        var index = TerrainCatalogValidator.Validate(catalog).Index!;
        var representatives = index.GetRepresentatives(TerrainCatalogFixture.Grass);

        Assert.Equal(
            new[]
            {
                TerrainCatalogFixture.Ref(0, 1),
                TerrainCatalogFixture.Ref(1, 1),
                TerrainCatalogFixture.Ref(0, 9)
            },
            representatives.Select(graphic => graphic.Reference).ToArray());
    }

    [Fact]
    public void Index_GetCandidatesForUnknownCenter_ReturnsEmpty()
    {
        var index = TerrainCatalogValidator.Validate(TerrainCatalogFixture.Valid()).Index!;
        var unknown = Guid.NewGuid();

        Assert.Empty(index.GetCandidates(unknown));
        Assert.Empty(index.GetRepresentatives(unknown));
    }

    [Fact]
    public void CreateValidated_ShuffledCatalog_HasEquivalentIndexes()
    {
        var baseCatalog = TerrainCatalogFixture.Valid();
        var catalogs = new[]
        {
            baseCatalog,
            new TerrainCatalog(baseCatalog.Terrains.Reverse().ToList(), baseCatalog.Graphics.Reverse().ToList()),
            new TerrainCatalog(
                baseCatalog.Terrains.Skip(1).Concat(baseCatalog.Terrains.Take(1)),
                baseCatalog.Graphics.Skip(2).Concat(baseCatalog.Graphics.Take(2)))
        };

        var descriptions = catalogs
            .Select(catalog => Describe(TerrainCatalogValidator.Validate(catalog).Index!))
            .ToList();

        Assert.Equal(descriptions[0], descriptions[1]);
        Assert.Equal(descriptions[0], descriptions[2]);
    }

    private static List<string> Describe(TerrainCatalogIndex index)
    {
        var entries = new List<string>();
        foreach (var id in new[] { TerrainCatalogFixture.Grass, TerrainCatalogFixture.Water })
        {
            entries.Add($"terrain:{id}:{index.GetDisplayColor(id)}");
            foreach (var group in index.GetCandidates(id))
            {
                entries.Add($"pattern:{id}:{group.Pattern}");
                entries.AddRange(group.Variants.Select(variant => $"variant:{id}:{variant.Reference}"));
            }
            entries.AddRange(index.GetRepresentatives(id).Select(graphic => $"representative:{id}:{graphic.Reference}"));
        }
        return entries;
    }
}
