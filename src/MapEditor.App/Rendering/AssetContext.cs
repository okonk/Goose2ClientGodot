using System;
using System.Collections.Generic;
using MapEditor.Rendering;
using MapEditor.Rendering.Terrain;

namespace MapEditor.App.Rendering;

internal sealed class AssetContext : IDisposable
{
    private AssetContext(
        SpriteAssetCache cache,
        MapRenderer renderer,
        IReadOnlyList<int> sheetIds,
        AppearanceAssetCatalog? appearance,
        AppearanceAvailability appearanceAvailability,
        TerrainAssetLoadResult terrain,
        AvaloniaTintedSpriteCache? tintCache = null,
        bool ownsResources = true)
    {
        Cache = cache;
        Renderer = renderer;
        SheetIds = sheetIds;
        Appearance = appearance;
        AppearanceAvailability = appearanceAvailability;
        Terrain = terrain;
        TintCache = tintCache ?? new AvaloniaTintedSpriteCache();
        _ownsResources = ownsResources;
    }

    private bool _ownsResources;

    public SpriteAssetCache Cache { get; }

    public MapRenderer Renderer { get; }

    public IReadOnlyList<int> SheetIds { get; }

    public AppearanceAssetCatalog? Appearance { get; }

    public AppearanceAvailability AppearanceAvailability { get; }

    public TerrainAssetLoadResult Terrain { get; }

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
            TerrainAssetLoadResult.Unavailable("Sprite assets are unavailable; load an asset directory to use the terrain tool."));
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

        TerrainAssetLoadResult terrain = TerrainAssetCatalog.Load(assetDirectory, cache.Manifest!);
        return new(cache, new MapRenderer(cache), sheetIds, appearance, availability, terrain);
    }

    internal AssetContext WithTerrain(TerrainAssetLoadResult terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        return new AssetContext(
            Cache,
            Renderer,
            SheetIds,
            Appearance,
            AppearanceAvailability,
            terrain,
            TintCache,
            ownsResources: false);
    }

    internal void TransferOwnershipTo(AssetContext replacement)
    {
        if (!_ownsResources || replacement._ownsResources || !ReferenceEquals(Cache, replacement.Cache) ||
            !ReferenceEquals(TintCache, replacement.TintCache))
        {
            throw new InvalidOperationException("Asset resource ownership cannot be transferred.");
        }

        _ownsResources = false;
        replacement._ownsResources = true;
    }

    public SpriteResolution Resolve(SpriteReference reference)
        => Cache.Resolve(reference);

    public IReadOnlyList<SpriteFrame> GetFrames(int sheet)
        => Cache.Manifest?.GetFrames(sheet) ?? Array.Empty<SpriteFrame>();

    public void Dispose()
    {
        if (!_ownsResources)
        {
            return;
        }

        _ownsResources = false;
        Exception? first = null;
        try
        {
            Cache.Dispose();
        }
        catch (Exception ex)
        {
            first = ex;
        }

        try
        {
            TintCache.Dispose();
        }
        catch (Exception ex)
        {
            if (first is null)
            {
                first = ex;
            }
            else
            {
                first.Data["AssetContext.AdditionalDisposeException"] = ex;
            }
        }

        if (first is not null)
        {
            throw first;
        }
    }
}
