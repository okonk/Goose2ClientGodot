using Godot;

namespace Goose2Client
{
    /// <summary>Shared lerp-tint shader material, port of Unity's material _Tint.
    /// The shader blends the source texture RGB toward a tint color using tint.a as the
    /// lerp factor; final alpha is always the texture's own alpha. Used for character slot
    /// dye and dropped-item ground tint.</summary>
    public static class TintMaterial
    {
        /// <summary>Cached canvas_item shader that performs <c>mix(tex.rgb, tint.rgb, tint.a)</c>
        /// and preserves the texture alpha.</summary>
        public static Shader Shader { get; } = new Shader
        {
            Code = @"shader_type canvas_item;
uniform vec4 tint : source_color = vec4(0.0);
void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    COLOR = vec4(mix(tex.rgb, tint.rgb, tint.a), tex.a) * COLOR;
}"
        };

        /// <summary>Creates a new ShaderMaterial configured with the given tint color.</summary>
        /// <param name="tint">Tint color — RGB is the target color, A is the lerp weight (0 = no tint).</param>
        public static ShaderMaterial Make(Color tint)
        {
            var mat = new ShaderMaterial { Shader = Shader };
            mat.SetShaderParameter("tint", tint);
            return mat;
        }
    }
}
