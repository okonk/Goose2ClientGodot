using Godot;

namespace Goose2Client
{
    /// <summary>Projection/culling math for world text — exact forward inverse of
    /// <see cref="WorldViewport.WindowToWorld"/> (pinned by the round-trip test).</summary>
    public static class WorldTextProjection
    {
        // No Vector2(Vector2I) ctor exists in GodotSharp — explicit component cast.
        public static Vector2 Project(Vector2 worldPos, Transform2D canvas, float scale, Vector2I origin)
            => canvas * worldPos * scale + new Vector2((float)origin.X, (float)origin.Y);

        /// <summary>True when the interiors are disjoint; flush on an inside edge is NOT culled — Rect2.Intersects requires interior overlap (probed).</summary>
        public static bool IsCulled(Rect2 element, Rect2 displayRect) => !element.Intersects(displayRect);
    }
}
