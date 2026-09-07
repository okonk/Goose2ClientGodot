using System;
using System.Collections.Generic;
using MapEditor.Rendering;

namespace MapEditor.App.Rendering;

internal sealed class AssetContext : IDisposable
{
    private AssetContext(
        SpriteAssetCache cache,
        MapRenderer renderer,
        IReadOnlyList<int> sheetIds,
        AppearanceAssetCatalog? appearance,
        AppearanceAvailability appearanceAvailability)
    {
        Cache = cache;
        Renderer = renderer;
        SheetIds = sheetIds;
        Appearance = appearance;
        AppearanceAvailability = appearanceAvailability;
    }

    public SpriteAssetCache Cache { get; }

    public MapRenderer Renderer { get; }

    public IReadOnlyList<int> SheetIds { get; }

    public AppearanceAssetCatalog? Appearance { get; }

    public AppearanceAvailability AppearanceAvailability { get; }

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
            AppearanceAvailability.Unavailable("Sprite assets are unavailable; load an asset directory to resolve appearance previews."));
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

        return new(cache, new MapRenderer(cache), sheetIds, appearance, availability);
    }

    public SpriteResolution Resolve(SpriteReference reference)
        => Cache.Resolve(reference);

    public IReadOnlyList<SpriteFrame> GetFrames(int sheet)
        => Cache.Manifest?.GetFrames(sheet) ?? Array.Empty<SpriteFrame>();

    public void Dispose()
        => Cache.Dispose();
}
