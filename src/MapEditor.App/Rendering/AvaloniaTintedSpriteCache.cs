using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MapEditor.GameData.Rows;
using MapEditor.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapEditor.App.Rendering;

internal sealed class AvaloniaTintedSpriteCache : IDisposable
{
    private readonly Dictionary<TintKey, TintedFrame> _frames = new();
    private bool _disposed;

    private readonly record struct TintKey(object Image, SpriteSourceRect SourceRect, RgbaValue Tint);

    public sealed record TintedFrame(Bitmap Bitmap, SpriteSourceRect SourceRect);

    public bool TryGet(AvaloniaSpriteSheetImage image, in SpriteSourceRect sourceRect, in RgbaValue tint, out TintedFrame frame)
    {
        frame = null!;
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);
        if (tint.A == 0)
        {
            frame = new TintedFrame(image.Bitmap, sourceRect);
            return true;
        }

        TintKey key = new(image, sourceRect, tint);
        if (!_frames.TryGetValue(key, out TintedFrame? cached))
        {
            // The cached frame is a standalone image, so its source rect is local to the frame.
            cached = new TintedFrame(
                BlendFrame(image.Bitmap, sourceRect, tint),
                new SpriteSourceRect(0, 0, sourceRect.Width, sourceRect.Height));
            _frames.Add(key, cached);
        }

        frame = cached;
        return true;
    }

    private static Bitmap BlendFrame(Bitmap source, in SpriteSourceRect rect, in RgbaValue tint)
    {
        if (source.Format != PixelFormat.Bgra8888)
        {
            throw new NotSupportedException($"Tint blending requires Bgra8888 pixels, got {source.Format}.");
        }

        int alpha = tint.A;
        int inverse = 255 - alpha;
        int rowLength = rect.Width * 4;
        IntPtr buffer = Marshal.AllocHGlobal(rowLength);
        byte[] row = new byte[rowLength];
        byte[] pixels = new byte[rowLength * rect.Height];
        try
        {
            using Image<Bgra32> blended = new(rect.Width, rect.Height);
            for (int y = 0; y < rect.Height; y++)
            {
                source.CopyPixels(new PixelRect(rect.X, rect.Y + y, rect.Width, 1), buffer, rowLength, rowLength);
                Marshal.Copy(buffer, row, 0, rowLength);
                int outputOffset = y * rowLength;
                for (int x = 0; x < rect.Width; x++)
                {
                    int offset = x * 4;
                    int sourceAlpha = row[offset + 3];
                    // Skia decodes PNG sheets premultiplied; the client blend operates on straight RGB.
                    int b = Unpremultiply(row[offset], sourceAlpha);
                    int g = Unpremultiply(row[offset + 1], sourceAlpha);
                    int r = Unpremultiply(row[offset + 2], sourceAlpha);
                    blended[x, y] = new Bgra32(
                        (byte)Blend(r, tint.R, alpha, inverse),
                        (byte)Blend(g, tint.G, alpha, inverse),
                        (byte)Blend(b, tint.B, alpha, inverse),
                        (byte)sourceAlpha);
                }
            }

            blended.CopyPixelDataTo(pixels.AsSpan());
            IntPtr data = Marshal.AllocHGlobal(pixels.Length);
            try
            {
                Marshal.Copy(pixels, 0, data, pixels.Length);
                return new Bitmap(
                    PixelFormat.Bgra8888,
                    AlphaFormat.Unpremul,
                    data,
                    new PixelSize(rect.Width, rect.Height),
                    new Vector(96, 96),
                    rowLength);
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int Unpremultiply(int premultiplied, int alpha)
        => alpha == 0 ? 0 : (int)Math.Round((double)premultiplied * 255.0 / alpha, MidpointRounding.AwayFromZero);

    // Locked client blend (Scripts/TintMaterial.cs): per-channel lerp of the source RGB toward the
    // tint RGB with the tint alpha as the factor; the source alpha is preserved exactly.
    private static int Blend(int source, int tintChannel, int alpha, int inverse)
        => (int)Math.Round((double)source * inverse / 255.0 + (double)tintChannel * alpha / 255.0, MidpointRounding.AwayFromZero);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (TintedFrame frame in _frames.Values)
        {
            frame.Bitmap.Dispose();
        }

        _frames.Clear();
    }
}
