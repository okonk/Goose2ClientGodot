using System;
using System.Collections.Generic;
using MapEditor.Rendering;

namespace MapEditor.Rendering.Tests.Fakes;

public sealed class FakeSpriteSheetLoader : ISpriteSheetLoader
{
    private readonly Func<string, SpriteSheetLoadResult> _behavior;

    public FakeSpriteSheetLoader(Func<string, SpriteSheetLoadResult>? behavior = null)
    {
        _behavior = behavior ?? (path => SpriteSheetLoadResult.Success(new FakeSpriteSheetImage(64, 64)));
    }

    public List<string> LoadedPaths { get; } = new();
    public int CallCount => LoadedPaths.Count;

    public SpriteSheetLoadResult Load(string path)
    {
        LoadedPaths.Add(path);
        return _behavior(path);
    }
}
