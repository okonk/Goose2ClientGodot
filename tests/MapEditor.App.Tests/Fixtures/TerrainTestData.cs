using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;
using MapEditor.Core.Terrain;
using MapEditor.Rendering;
using MapEditor.Rendering.Terrain;

namespace MapEditor.App.Tests.Fixtures;

internal static class TerrainTestData
{
    internal static TerrainAssetLoadResult Result(params (string Name, int Graphic)[] definitions)
    {
        TerrainCatalog catalog = Catalog(definitions);
        string frames = string.Join(",", definitions.Select(definition => $"\"{definition.Graphic}\":[0,0,32,32]"));
        return TerrainAssetCatalog.Validate(catalog, SpriteManifest.Parse($"{{\"tileSize\":32,\"sheets\":{{\"1\":{{{frames}}}}}}}"));
    }

    internal static TerrainCatalog Catalog(params (string Name, int Graphic)[] definitions)
    {
        var sets = new List<TerrainSetDefinition>();
        foreach ((string name, int graphic) in definitions)
        {
            var reference = new TerrainGraphicReference(1, graphic);
            sets.Add(new TerrainSetDefinition(
                TerrainGeneratedId.Create(TerrainTopology.FourWay, new[] { reference }),
                name,
                TerrainReviewStatus.Enabled,
                TerrainTopology.FourWay,
                new TerrainSetMetrics(1, 1, 1, 1, 0, 1, 0, 1, 1, 0, 1),
                TerrainMasks.Required(TerrainTopology.FourWay)
                    .Select(mask => new TerrainMaskDefinition(mask, new[] { reference })),
                new[] { new TerrainMemberDefinition(reference, TerrainMemberProvenance.MapObserved) },
                Array.Empty<TerrainDiagnostic>()));
        }

        return new TerrainCatalog(
            TerrainCatalogJson.CurrentSchemaVersion,
            "test",
            "sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
            new TerrainGenerationSettings(2, 1, 1, 1, 1, 0.1, 0.5, 0.5, 0.1, 0.5, 0.9, 0.9),
            sets,
            Array.Empty<TerrainDiagnostic>());
    }
}
