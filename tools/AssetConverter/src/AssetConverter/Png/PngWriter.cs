using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Goose2.AssetConverter.Png;

/// <summary>Utility for writing RGBA8 buffers to PNG files.</summary>
public static class PngWriter
{
    /// <summary>Writes a top-down RGBA8 buffer to a PNG. No vertical flip — see Task 3
    /// orientation note: matches Unity's on-disk PNG orientation.</summary>
    public static void Write(byte[] rgba, int width, int height, string path)
    {
        using var image = Image.LoadPixelData<Rgba32>(rgba, width, height);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        image.SaveAsPng(path);
    }
}
