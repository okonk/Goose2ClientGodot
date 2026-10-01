using Godot;
using System.Collections.Generic;

namespace Goose2Client.UI;

/// <summary>
/// Which screen points the HUD windows cover. Godot GUI picking cannot answer this:
/// window art and Content are MouseFilter.Ignore, so a point over a window frame picks
/// nothing — the window root rects are the occluders.
/// </summary>
public static class WindowOcclusion
{
    public static bool IsPointCovered(Vector2 point) => IsPointInsideAny(VisibleWindowRects(), point);

    public static bool IsPointInsideAny(IEnumerable<Rect2> rects, Vector2 point)
    {
        foreach (var rect in rects)
            if (rect.HasPoint(point))
                return true;
        return false;
    }

    public static IEnumerable<Rect2> VisibleWindowRects()
    {
        var ui = GameManager.Instance?.UiLayer;
        if (ui == null || !GodotObject.IsInstanceValid(ui)) yield break;

        foreach (var c in WindowRoots(ui, 0))
            yield return new Rect2(c.GlobalPosition, c.Size);
    }

    // Windows are HUD children; server-spawned ones sit under a manager node one further down
    // (a plain Node). A window's own subtree holds no windows, so the walk stops at one — this
    // runs per mouse event.
    private static IEnumerable<Control> WindowRoots(Node root, int depth)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is BaseWindow or VitalsWindow or PartyWindow or BuffEffectsWindow or MinimapControl)
            {
                if (child is Control w && w.IsVisibleInTree())
                    yield return w;
                continue;
            }
            if (depth < 2)
                foreach (var nested in WindowRoots(child, depth + 1))
                    yield return nested;
        }
    }
}
