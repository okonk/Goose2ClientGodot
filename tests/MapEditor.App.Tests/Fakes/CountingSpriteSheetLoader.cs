using System;
using System.Collections.Generic;
using MapEditor.Rendering;

namespace MapEditor.App.Tests.Fakes;

public sealed class CountingSpriteSheetImage : ISpriteSheetImage
{
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public int DisposeCount { get; private set; }
    public bool Disposed => DisposeCount > 0;

    public CountingSpriteSheetImage(int pixelWidth, int pixelHeight)
    {
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    public void Dispose()
        => DisposeCount++;
}

public sealed class CountingSpriteSheetLoader : ISpriteSheetLoader
{
    private readonly Func<string, SpriteSheetLoadResult> _behavior;

    public CountingSpriteSheetLoader(Func<string, SpriteSheetLoadResult>? behavior = null)
    {
        _behavior = behavior ?? (path => SpriteSheetLoadResult.Success(new CountingSpriteSheetImage(64, 64)));
    }

    public List<string> LoadedPaths { get; } = new();
    public int CallCount => LoadedPaths.Count;

    public SpriteSheetLoadResult Load(string path)
    {
        LoadedPaths.Add(path);
        return _behavior(path);
    }
}
