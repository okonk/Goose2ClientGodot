using System;
using System.Collections.Generic;
using System.IO;
using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.Rendering;

internal sealed class AssetContext : IDisposable
{
    private AssetContext(
        SpriteAssetCache cache,
        MapRenderer renderer,
        IReadOnlyList<int> sheetIds,
        AppearanceAssetCatalog? appearance,
        AppearanceAvailability appearanceAvailability,
        GraphicAssetCatalog? graphics,
        GraphicViewerAvailability graphicViewerAvailability,
        TerrainCatalogLoadResult terrain)
    {
        Cache = cache;
        Renderer = renderer;
        SheetIds = sheetIds;
        Appearance = appearance;
        AppearanceAvailability = appearanceAvailability;
        Graphics = graphics;
        GraphicViewerAvailability = graphicViewerAvailability;
        Terrain = terrain;
        TintCache = new AvaloniaTintedSpriteCache();
    }

    public SpriteAssetCache Cache { get; }

    public MapRenderer Renderer { get; }

    public IReadOnlyList<int> SheetIds { get; }

    public AppearanceAssetCatalog? Appearance { get; }

    public AppearanceAvailability AppearanceAvailability { get; }

    public GraphicAssetCatalog? Graphics { get; }

    public GraphicViewerAvailability GraphicViewerAvailability { get; }

    public TerrainCatalogLoadResult Terrain { get; }

    public AvaloniaTintedSpriteCache TintCache { get; }

    public bool IsAvailable => Cache.IsAvailable;

    public bool IsDisposed => Cache.IsDisposed;

    public static AssetContext CreateUnavailable()
    {
        SpriteAssetCache cache = SpriteAssetCache.CreateUnavailable();
        return new(
            cache,
            new MapRenderer(cache),
            Array.Empty<int>(),
            null,
            AppearanceAvailability.Unavailable("Sprite assets are unavailable; load an asset directory to resolve appearance previews."),
            null,
            GraphicViewerAvailability.Unavailable("Sprite assets are unavailable; load an asset directory to resolve graphic viewer previews."),
            TerrainCatalogLoadResult.Unavailable(TerrainAssetCatalog.FileName));
    }

    public static AssetContext Create(string assetDirectory, ISpriteSheetLoader loader)
    {
        SpriteAssetCache cache = SpriteAssetCache.Open(assetDirectory, loader);
        IReadOnlyList<int> sheetIds = TileSheetFilter.Apply(cache.Manifest!.SheetIds, TileSheetFilter.Load(assetDirectory));
        AppearanceAssetCatalog? appearance = null;
        AppearanceAvailability availability;
        try
        {
            appearance = new AppearanceAssetCatalog(AppearanceManifest.Load(assetDirectory), cache);
            availability = AppearanceAvailability.Available;
        }
        catch (AppearanceManifestException ex)
        {
            availability = AppearanceAvailability.Unavailable(ex.Message);
        }

        GraphicAssetCatalog? graphics = null;
        GraphicViewerAvailability graphicViewerAvailability;
        try
        {
            graphics = GraphicAssetCatalog.Create(cache.Manifest!, GraphicAnimationManifest.Load(assetDirectory));
            graphicViewerAvailability = GraphicViewerAvailability.Available;
        }
        catch (GraphicAnimationManifestException ex)
        {
            graphicViewerAvailability = GraphicViewerAvailability.Unavailable(ex.Message);
        }

        string terrainSourcePath = Path.Combine(Path.GetFullPath(assetDirectory), TerrainAssetCatalog.FileName);
        TerrainCatalogLoadResult terrain;
        try
        {
            terrain = TerrainAssetCatalog.Load(assetDirectory, cache.Manifest!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            terrain = TerrainCatalogLoadResult.Invalid(
                terrainSourcePath,
                TerrainFileRevision.Missing,
                Array.Empty<TerrainValidationIssue>(),
                $"Failed to load terrain catalog: {ex.Message}");
        }

        return new(cache, new MapRenderer(cache), sheetIds, appearance, availability, graphics, graphicViewerAvailability, terrain);
    }

    public SpriteResolution Resolve(SpriteReference reference)
        => Cache.Resolve(reference);

    public IReadOnlyList<SpriteFrame> GetFrames(int sheet)
        => Cache.Manifest?.GetFrames(sheet) ?? Array.Empty<SpriteFrame>();

    public void Dispose()
    {
        Cache.Dispose();
        TintCache.Dispose();
    }
}
