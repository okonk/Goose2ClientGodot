using System;
using Avalonia.Media.Imaging;
using MapEditor.Rendering;

namespace MapEditor.App.Rendering;

internal sealed class AvaloniaSpriteSheetImage : ISpriteSheetImage
{
    private readonly Bitmap _bitmap;
    private bool _disposed;

    public AvaloniaSpriteSheetImage(Bitmap bitmap)
    {
        _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
    }

    public Bitmap Bitmap => _bitmap;

    public int PixelWidth
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _bitmap.PixelSize.Width;
        }
    }

    public int PixelHeight
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _bitmap.PixelSize.Height;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _bitmap.Dispose();
    }
}
