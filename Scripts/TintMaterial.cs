using System.Collections.Generic;
using Godot;

namespace Goose2Client
{
    /// <summary>Shared tint shader material, port of Unity's material _Tint.
    /// The shader blends the source texture RGB toward a tint color using tint.a as the
    /// lerp factor; final alpha is always the texture's own alpha. The sprite's own light/dark
    /// variation around its mean luminance is added back onto the tint so dyed art keeps its shading.</summary>
    public static class TintMaterial
    {
        private static readonly Dictionary<ulong, float> MeanLuminanceCache = new();

        public static Shader Shader { get; } = new Shader
        {
            Code = @"shader_type canvas_item;
uniform vec4 tint : source_color = vec4(0.0);
uniform float mean_lum = 0.5;
// COLOR arrives as texture * modulate in Godot 4; sampling TEXTURE and multiplying by COLOR again darkened every dyed sprite.
void fragment() {
    float l = dot(COLOR.rgb, vec3(0.299, 0.587, 0.114));
    vec3 dyed = clamp(tint.rgb + (l - mean_lum), 0.0, 1.0);
    COLOR = vec4(mix(COLOR.rgb, dyed, tint.a), COLOR.a);
}"
        };

        public static ShaderMaterial Make(Color tint, Texture2D? texture)
        {
            var mat = new ShaderMaterial { Shader = Shader };
            mat.SetShaderParameter("tint", tint);
            mat.SetShaderParameter("mean_lum", MeanLuminance(texture));
            return mat;
        }

        public static void Apply(CanvasItem item, Texture2D? texture, Color tint)
        {
            if (tint.A <= 0f)
            {
                item.Material = null;
                return;
            }
            if (item.Material is not ShaderMaterial mat || mat.Shader != Shader)
                item.Material = mat = new ShaderMaterial { Shader = Shader };
            mat.SetShaderParameter("tint", tint);
            mat.SetShaderParameter("mean_lum", MeanLuminance(texture));
        }

        public static Texture2D? RestingFrame(SpriteFrames? frames)
        {
            if (frames == null) return null;
            foreach (var name in new[] { "idle-down", "idle-equip-down", "idle-no-equip-down" })
            {
                if (frames.HasAnimation(name) && frames.GetFrameCount(name) > 0)
                    return frames.GetFrameTexture(name, 0);
            }
            var animations = frames.GetAnimationNames();
            return animations.Length > 0 && frames.GetFrameCount(animations[0]) > 0
                ? frames.GetFrameTexture(animations[0], 0)
                : null;
        }

        public static float MeanLuminance(Texture2D? texture)
        {
            if (texture == null) return 0.5f;
            var key = texture.GetInstanceId();
            if (MeanLuminanceCache.TryGetValue(key, out var cached)) return cached;

            var image = texture.GetImage();
            if (image == null) return 0.5f;
            if (image.IsCompressed()) image.Decompress();

            double sum = 0;
            int count = 0;
            for (int y = 0; y < image.GetHeight(); y++)
            {
                for (int x = 0; x < image.GetWidth(); x++)
                {
                    var c = image.GetPixel(x, y);
                    if (c.A <= 0f) continue;
                    sum += 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                    count++;
                }
            }
            var mean = count > 0 ? (float)(sum / count) : 0.5f;
            MeanLuminanceCache[key] = mean;
            return mean;
        }
    }
}
