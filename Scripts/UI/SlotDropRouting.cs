using System.Collections.Generic;
using Godot;

namespace Goose2Client.UI;

public static class SlotDropRouting
{
    public static int NearestSlot(IReadOnlyList<Rect2> rects, Vector2 point)
    {
        int best = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < rects.Count; i++)
        {
            var rect = rects[i];
            if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
                continue;

            if (rect.HasPoint(point))
                return i;

            float distance = (rect.GetCenter() - point).LengthSquared();
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }
}
