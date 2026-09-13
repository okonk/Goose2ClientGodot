using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MapEditor.Core;

public readonly record struct TerrainGraphicReference(int Sheet, int Graphic);

public sealed record TerrainDefinition(Guid Id, string Name, TerrainColor? ColorOverride)
{
    // Display color derives from the stable ID so renaming never changes it.
    public TerrainColor DisplayColor => ColorOverride ?? TerrainColor.Derive(Id);
}

public sealed record TerrainGraphicDefinition(
    TerrainGraphicReference Reference,
    TerrainPattern Pattern);

public sealed record TerrainCatalog
{
    public IReadOnlyList<TerrainDefinition> Terrains { get; }

    public IReadOnlyList<TerrainGraphicDefinition> Graphics { get; }

    public TerrainCatalog(IEnumerable<TerrainDefinition> terrains, IEnumerable<TerrainGraphicDefinition> graphics)
    {
        Terrains = new ReadOnlyCollection<TerrainDefinition>(terrains.ToList());
        Graphics = new ReadOnlyCollection<TerrainGraphicDefinition>(graphics.ToList());
    }

    public bool Equals(TerrainCatalog? other)
    {
        return other is not null
            && Terrains.SequenceEqual(other.Terrains)
            && Graphics.SequenceEqual(other.Graphics);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var terrain in Terrains)
            {
                hash = hash * 31 + terrain.GetHashCode();
            }

            foreach (var graphic in Graphics)
            {
                hash = hash * 31 + graphic.GetHashCode();
            }

            return hash;
        }
    }
}
