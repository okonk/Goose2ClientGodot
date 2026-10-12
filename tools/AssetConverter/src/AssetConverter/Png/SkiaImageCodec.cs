using System.Runtime.InteropServices;
using SkiaSharp;

namespace Goose2.AssetConverter.Png;

internal static class SkiaImageCodec
{
    public static byte[] DecodeRgba(byte[] encoded, out int width, out int height)
    {
        using MemoryStream input = new(encoded, writable: false);
        using SKCodec codec = SKCodec.Create(input)
            ?? throw new InvalidDataException("The image payload could not be decoded.");
        width = codec.Info.Width;
        height = codec.Info.Height;
        SKImageInfo info = new(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        byte[] rgba = new byte[checked(info.RowBytes * height)];
        SKCodecResult result = codec.GetPixels(info, rgba);
        if (result != SKCodecResult.Success)
        {
            throw new InvalidDataException($"The image payload could not be decoded ({result}).");
        }

        return rgba;
    }

    public static void WritePng(byte[] rgba, int width, int height, string path)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length != checked(width * height * 4))
        {
            throw new ArgumentException("The RGBA buffer length does not match the image dimensions.", nameof(rgba));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SKImageInfo info = new(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using SKBitmap bitmap = new(info);
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("The PNG encoder failed.");
        using FileStream output = File.Create(path);
        encoded.SaveTo(output);
    }
}
