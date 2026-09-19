using Godot;
using Goose2Client.Character;

namespace Goose2Client.UI;

public static class CustomPreviewMetrics
{
    public static float Scale(Vector2 controlSize)
    {
        if (controlSize.X <= 0f || controlSize.Y <= 0f) return 0f;
        return 3.0f;
    }

    public static (Vector2 Size, Vector2 Position) Layout(Vector2 frameSize, Vector2 controlSize)
    {
        float s = Scale(controlSize);
        var size = frameSize * s;
        // A 64px frame occupies 56px above the ground line (CharacterAnchor.OffsetY).
        float groundY = controlSize.Y * 56f / 64f;
        var center = new Vector2(controlSize.X / 2f, groundY + CharacterAnchor.OffsetY((int)frameSize.Y) * s);
        return (size, center - size / 2f);
    }
}
