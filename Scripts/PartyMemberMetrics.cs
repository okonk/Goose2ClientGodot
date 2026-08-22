using Godot;

namespace Goose2Client;

public static class PartyMemberMetrics
{
    public static Vector2I MinSize(float factor)
        => new(UiScale.ScaleSize(87f, factor), UiScale.ScaleSize(33f, factor));
}
