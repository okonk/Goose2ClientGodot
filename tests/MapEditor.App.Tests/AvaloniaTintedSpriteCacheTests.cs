using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using MapEditor.App.Rendering;
using MapEditor.GameData.Rows;
using MapEditor.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace MapEditor.App.Tests;

public class AvaloniaTintedSpriteCacheTests
{
    private static readonly RgbaValue Pixel00 = new(200, 100, 50, 255);
    private static readonly RgbaValue Pixel10 = new(10, 20, 30, 128);
    private static readonly RgbaValue Pixel01 = new(0, 0, 0, 0);
    private static readonly RgbaValue Pixel11 = new(255, 255, 255, 255);

    private static Bitmap CreateSheet(params (int X, int Y, RgbaValue Pixel)[] pixels)
    {
        int width = 0;
        int height = 0;
        foreach ((int x, int y, _) in pixels)
        {
            width = Math.Max(width, x + 1);
            height = Math.Max(height, y + 1);
        }

        using Image<Rgba32> image = new(width, height);
        foreach ((int x, int y, RgbaValue pixel) in pixels)
        {
            image[x, y] = new Rgba32((byte)pixel.R, (byte)pixel.G, (byte)pixel.B, (byte)pixel.A);
        }

        using MemoryStream encoded = new();
        image.SaveAsPng(encoded);
        return new Bitmap(new MemoryStream(encoded.ToArray()));
    }

    private static int Blend(int source, int tint, int alpha)
        => (int)Math.Round((double)source * (255 - alpha) / 255.0 + (double)tint * alpha / 255.0, MidpointRounding.AwayFromZero);

    private static (byte R, byte G, byte B, byte A) Pixel(AvaloniaTintedSpriteCache.TintedFrame frame, int x, int y)
    {
        Bitmap bitmap = frame.Bitmap;
        IntPtr buffer = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), buffer, 4, 4);
            byte[] pixel = new byte[4];
            Marshal.Copy(buffer, pixel, 0, 4);
            return (pixel[2], pixel[1], pixel[0], pixel[3]);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [AvaloniaFact]
    public void TryGet_ZeroAlpha_ReturnsTheUntintedSheetFrame()
    {
        using Bitmap sheet = CreateSheet((0, 0, Pixel00), (1, 0, Pixel10), (0, 1, Pixel01), (1, 1, Pixel11));
        using AvaloniaSpriteSheetImage image = new(sheet);
        using AvaloniaTintedSpriteCache cache = new();
        SpriteSourceRect rect = new(0, 0, 2, 2);

        bool found = cache.TryGet(image, rect, new RgbaValue(200, 100, 50, 0), out AvaloniaTintedSpriteCache.TintedFrame frame);

        Assert.True(found);
        Assert.Same(sheet, frame.Bitmap);
        Assert.Equal(rect, frame.SourceRect);
    }

    [AvaloniaFact]
    public void TryGet_MidAlphaMatchesLockedIntegerBlendAndPreservesAlphaByteForByte()
    {
        using Bitmap sheet = CreateSheet((0, 0, Pixel00), (1, 0, Pixel10), (0, 1, Pixel01), (1, 1, Pixel11));
        using AvaloniaSpriteSheetImage image = new(sheet);
        using AvaloniaTintedSpriteCache cache = new();
        RgbaValue tint = new(200, 100, 50, 128);

        cache.TryGet(image, new SpriteSourceRect(0, 0, 2, 2), tint, out AvaloniaTintedSpriteCache.TintedFrame frame);

        Assert.NotSame(sheet, frame.Bitmap);
        Assert.Equal(new SpriteSourceRect(0, 0, 2, 2), frame.SourceRect);
        AssertPixel(frame, 0, 0, Pixel00, tint);
        AssertPixel(frame, 1, 0, Pixel10, tint);
        AssertPixel(frame, 0, 1, Pixel01, tint);
        AssertPixel(frame, 1, 1, Pixel11, tint);
    }

    [AvaloniaFact]
    public void TryGet_FullAlphaReplacesRgbChannelsButPreservesSourceAlpha()
    {
        using Bitmap sheet = CreateSheet((0, 0, Pixel00), (1, 0, Pixel10), (0, 1, Pixel01), (1, 1, Pixel11));
        using AvaloniaSpriteSheetImage image = new(sheet);
        using AvaloniaTintedSpriteCache cache = new();
        RgbaValue tint = new(200, 100, 50, 255);

        cache.TryGet(image, new SpriteSourceRect(0, 0, 2, 2), tint, out AvaloniaTintedSpriteCache.TintedFrame frame);

        AssertPixel(frame, 0, 0, Pixel00, tint);
        AssertPixel(frame, 1, 0, Pixel10, tint);
        AssertPixel(frame, 0, 1, Pixel01, tint);
        AssertPixel(frame, 1, 1, Pixel11, tint);
    }

    [AvaloniaFact]
    public void TryGet_RepeatedKeyReusesOneBitmapAndTintRectImageChangesGenerateDistinctFrames()
    {
        using Bitmap sheet = CreateSheet(
            (0, 0, Pixel00), (1, 0, Pixel10), (0, 1, Pixel01), (1, 1, Pixel11),
            (2, 0, Pixel11), (3, 0, Pixel01), (2, 1, Pixel10), (3, 1, Pixel00));
        using AvaloniaSpriteSheetImage image = new(sheet);
        using Bitmap otherSheet = CreateSheet((0, 0, Pixel10), (1, 0, Pixel00), (0, 1, Pixel11), (1, 1, Pixel01));
        using AvaloniaSpriteSheetImage otherImage = new(otherSheet);
        using AvaloniaTintedSpriteCache cache = new();
        RgbaValue tint = new(200, 100, 50, 128);
        SpriteSourceRect rect = new(0, 0, 2, 2);

        cache.TryGet(image, rect, tint, out AvaloniaTintedSpriteCache.TintedFrame first);
        cache.TryGet(image, rect, tint, out AvaloniaTintedSpriteCache.TintedFrame repeated);
        cache.TryGet(image, rect, new RgbaValue(200, 100, 50, 129), out AvaloniaTintedSpriteCache.TintedFrame otherTint);
        cache.TryGet(image, new SpriteSourceRect(2, 0, 2, 2), tint, out AvaloniaTintedSpriteCache.TintedFrame otherRect);
        cache.TryGet(otherImage, rect, tint, out AvaloniaTintedSpriteCache.TintedFrame otherImageFrame);

        Assert.Same(first.Bitmap, repeated.Bitmap);
        Assert.NotSame(first.Bitmap, otherTint.Bitmap);
        Assert.NotSame(first.Bitmap, otherRect.Bitmap);
        Assert.NotSame(first.Bitmap, otherImageFrame.Bitmap);
    }

    [AvaloniaFact]
    public void Dispose_DisposesEveryGeneratedFrameAndRejectsLaterRequests()
    {
        using Bitmap sheet = CreateSheet((0, 0, Pixel00), (1, 0, Pixel10), (0, 1, Pixel01), (1, 1, Pixel11));
        using AvaloniaSpriteSheetImage image = new(sheet);
        AvaloniaTintedSpriteCache cache = new();
        cache.TryGet(image, new SpriteSourceRect(0, 0, 2, 2), new RgbaValue(200, 100, 50, 128), out AvaloniaTintedSpriteCache.TintedFrame first);
        cache.TryGet(image, new SpriteSourceRect(0, 0, 2, 2), new RgbaValue(10, 20, 30, 64), out AvaloniaTintedSpriteCache.TintedFrame second);
        Bitmap firstBitmap = first.Bitmap;
        Bitmap secondBitmap = second.Bitmap;

        cache.Dispose();

        Assert.True(IsDisposed(firstBitmap));
        Assert.True(IsDisposed(secondBitmap));
        Assert.Throws<ObjectDisposedException>(() => cache.TryGet(image, new SpriteSourceRect(0, 0, 2, 2), new RgbaValue(0, 0, 0, 128), out _));
        cache.Dispose();
    }

    private static bool IsDisposed(Bitmap bitmap)
    {
        try
        {
            _ = bitmap.PixelSize;
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static void AssertPixel(AvaloniaTintedSpriteCache.TintedFrame frame, int x, int y, RgbaValue source, RgbaValue tint)
    {
        (byte r, byte g, byte b, byte a) = Pixel(frame, x, y);
        if (source.A == 0)
        {
            Assert.Equal((byte)0, r);
            Assert.Equal((byte)0, g);
            Assert.Equal((byte)0, b);
        }
        else
        {
            Assert.Equal((byte)Blend(source.R, tint.R, tint.A), r);
            Assert.Equal((byte)Blend(source.G, tint.G, tint.A), g);
            Assert.Equal((byte)Blend(source.B, tint.B, tint.A), b);
        }

        Assert.Equal((byte)source.A, a);
    }
}
