using System;
using MapEditor.Rendering;

namespace MapEditor.Rendering.Tests.Fakes;

public sealed class FakeSpriteSheetImage : ISpriteSheetImage
{
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public bool ThrowOnDispose { get; set; }
    public int DisposeCount { get; private set; }
    public bool Disposed => DisposeCount > 0;

    public FakeSpriteSheetImage(int pixelWidth, int pixelHeight)
    {
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    public void Dispose()
    {
        DisposeCount++;
        if (ThrowOnDispose)
        {
            throw new InvalidOperationException("Fake image disposal failed.");
        }
    }
}
