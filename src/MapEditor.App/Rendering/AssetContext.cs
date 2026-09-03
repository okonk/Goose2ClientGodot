using System;
using System.Collections.Generic;
using MapEditor.Rendering;

namespace MapEditor.App.Rendering;

internal sealed class AssetContext : IDisposable
{
    private AssetContext(SpriteAssetCache cache, MapRenderer renderer, IReadOnlyList<int> sheetIds)
    {
        Cache = cache;
        Renderer = renderer;
        SheetIds = sheetIds;
    }

    public SpriteAssetCache Cache { get; }

    public MapRenderer Renderer { get; }

    public IReadOnlyList<int> SheetIds { get; }

    public bool IsAvailable => Cache.IsAvailable;

    public bool IsDisposed => Cache.IsDisposed;

    public static AssetContext CreateUnavailable()
    {
        SpriteAssetCache cache = SpriteAssetCache.CreateUnavailable();
        return new(cache, new MapRenderer(cache), Array.Empty<int>());
    }

    public static AssetContext Create(string assetDirectory, ISpriteSheetLoader loader)
    {
        SpriteAssetCache cache = SpriteAssetCache.Open(assetDirectory, loader);
        return new(cache, new MapRenderer(cache), cache.Manifest!.SheetIds);
    }

    public SpriteResolution Resolve(SpriteReference reference)
        => Cache.Resolve(reference);

    public IReadOnlyList<SpriteFrame> GetFrames(int sheet)
        => Cache.Manifest?.GetFrames(sheet) ?? Array.Empty<SpriteFrame>();

    public void Dispose()
        => Cache.Dispose();
}
