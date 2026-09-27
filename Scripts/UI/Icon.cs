using Godot;
namespace Goose2Client.UI;

public static class Icon
{
    /// <summary>Show graphic (file,id) tinted by rgba (0-255) in the TextureRect, or hide it.</summary>
    public static void Apply(TextureRect rect, int file, int id, int r, int g, int b, int a)
    {
        var tex = GameManager.Instance.Sprites.Get(file, id);
        rect.Texture = tex;
        rect.Visible = tex != null;
        rect.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        if (tex == null) return;
        TintMaterial.Apply(rect, tex, a > 0 ? new Color(r / 255f, g / 255f, b / 255f, a / 255f) : default);
    }

    public static void Clear(TextureRect rect) { rect.Texture = null; rect.Visible = false; rect.Material = null; }
}
