using Godot;

namespace Goose2Client.UI;

public static class CustomPreviewMetrics
{
    public static (Vector2 Size, Vector2 Position) Layout(Vector2 textureSize, Vector2 controlSize)
    {
        if (textureSize.X <= 0f || textureSize.Y <= 0f)
            return (Vector2.Zero, new Vector2(controlSize.X / 2f, controlSize.Y));

        float scale = Mathf.Min(controlSize.X / textureSize.X, controlSize.Y / textureSize.Y);
        var size = textureSize * scale;
        var pos = new Vector2((controlSize.X - size.X) / 2f, controlSize.Y - size.Y);
        return (size, pos);
    }
}
