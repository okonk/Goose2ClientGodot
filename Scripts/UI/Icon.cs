using Godot;
namespace Goose2Client.UI;

public static class Icon
{
    private static Shader _tintShader;
    // Faithful to Character.cs:205-214 — tint.a is a BLEND factor, not opacity.
    private static Shader TintShader => _tintShader ??= new Shader { Code = @"shader_type canvas_item;
uniform vec4 tint : source_color = vec4(0.0);
void fragment() { vec4 t = texture(TEXTURE, UV); COLOR = vec4(mix(t.rgb, tint.rgb, tint.a), t.a) * COLOR; }" };

    /// <summary>Show graphic (file,id) tinted by rgba (0-255) in the TextureRect, or hide it.</summary>
    public static void Apply(TextureRect rect, int file, int id, int r, int g, int b, int a)
    {
        var tex = GameManager.Instance.Sprites.Get(file, id);
        rect.Texture = tex;
        rect.Visible = tex != null;
        rect.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        if (tex == null) return;
        if (a > 0)
        {
            if (rect.Material is not ShaderMaterial m) rect.Material = m = new ShaderMaterial { Shader = TintShader };
            m.SetShaderParameter("tint", new Color(r / 255f, g / 255f, b / 255f, a / 255f));
        }
        else rect.Material = null;
    }

    public static void Clear(TextureRect rect) { rect.Texture = null; rect.Visible = false; rect.Material = null; }
}
