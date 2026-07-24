using Goose2.AssetConverter.Gif;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Goose2.AssetConverter.Png;

/// <summary>Decodes a graphic ADF payload (GIF or BMP) to a top-down RGBA8 buffer with the
/// original clients' transparency rule applied: near-black (r&lt;=1, g==0, b==0) is transparent.
/// For GIF this is GifLoader's existing behavior (GifLoader.cs:100); for BMP (Aspereta) the same
/// rule is applied here — the original Aspereta client color-keys on constant black.</summary>
public static class PayloadDecoder
{
    public static byte[] ToRgba(byte[] payload, out int width, out int height)
    {
        if (payload.Length >= 3 && payload[0] == 'G' && payload[1] == 'I' && payload[2] == 'F')
            return GifLoader.Load(payload, out width, out height);

        if (payload.Length >= 2 && payload[0] == 'B' && payload[1] == 'M')
        {
            using var image = Image.Load<Rgba32>(payload);
            width = image.Width;
            height = image.Height;
            var rgba = new byte[width * height * 4];
            image.CopyPixelDataTo(rgba);
            for (int i = 0; i < rgba.Length; i += 4)
            {
                if (rgba[i] <= 1 && rgba[i + 1] == 0 && rgba[i + 2] == 0)
                    rgba[i + 3] = 0;
            }
            return rgba;
        }

        throw new NotSupportedException(
            $"Unsupported payload format (first bytes: 0x{payload.ElementAtOrDefault(0):X2} 0x{payload.ElementAtOrDefault(1):X2})");
    }
}
