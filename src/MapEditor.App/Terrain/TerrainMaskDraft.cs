using MapEditor.Core;
using MapEditor.Core.Terrain;
using MapEditor.Rendering;

namespace MapEditor.App.Terrain;

internal enum TerrainFrameState
{
    Resolved,
    Missing,
    InvalidSize
}

internal sealed record TerrainVariantDraft
{
    internal TerrainGraphicReference Reference { get; }
    internal TerrainFrameState FrameState { get; }
    internal SpriteSourceRect? SourceRect { get; }

    internal TerrainVariantDraft(TerrainGraphicReference reference, SpriteManifest manifest)
    {
        Reference = reference;
        if (!manifest.TryGetSourceRect(new SpriteReference(reference.Sheet, reference.Graphic), out SpriteSourceRect rect))
        {
            FrameState = TerrainFrameState.Missing;
            return;
        }

        SourceRect = rect;
        FrameState = rect.Width == SpriteManifest.RequiredTileSize && rect.Height == SpriteManifest.RequiredTileSize
            ? TerrainFrameState.Resolved
            : TerrainFrameState.InvalidSize;
    }
}

internal sealed record TerrainMaskDraft
{
    internal int Mask { get; }
    internal IReadOnlyList<TerrainVariantDraft> Variants { get; }
    internal bool North => (Mask & TerrainMasks.North) != 0;
    internal bool East => (Mask & TerrainMasks.East) != 0;
    internal bool South => (Mask & TerrainMasks.South) != 0;
    internal bool West => (Mask & TerrainMasks.West) != 0;
    internal bool NorthEast => (Mask & TerrainMasks.NorthEast) != 0;
    internal bool SouthEast => (Mask & TerrainMasks.SouthEast) != 0;
    internal bool SouthWest => (Mask & TerrainMasks.SouthWest) != 0;
    internal bool NorthWest => (Mask & TerrainMasks.NorthWest) != 0;
    internal TerrainGraphicReference? Representative => Variants.Count == 0 ? null : Variants[0].Reference;

    internal TerrainMaskDraft(int mask, IEnumerable<TerrainGraphicReference> variants, SpriteManifest manifest)
    {
        Mask = mask;
        Variants = Array.AsReadOnly(variants.Select(reference => new TerrainVariantDraft(reference, manifest)).ToArray());
    }
}
