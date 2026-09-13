using System.Collections.Generic;

namespace MapEditor.Core;

public sealed class TerrainResolvedPatch
{
    public int Layer { get; }

    public IReadOnlyDictionary<int, MapTileLayer> Changes { get; }

    public int Count => Changes.Count;

    internal TerrainResolvedPatch(int layer, IReadOnlyDictionary<int, MapTileLayer> changes)
    {
        Layer = layer;
        Changes = changes;
    }
}
