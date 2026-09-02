using System;

namespace MapEditor.Rendering;

public interface ISpriteSheetImage : IDisposable
{
    int PixelWidth { get; }
    int PixelHeight { get; }
}
