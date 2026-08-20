using Godot;

namespace Goose2Client
{
    /// <summary>Pure math for world-text projection and culling (no nodes). Forward twin of
    /// <see cref="WorldViewport.WindowToWorld"/> (its exact inverse); see the round-trip test.</summary>
    public static class WorldTextProjection
    {
        /// <summary>World (map) px → root-window px through the sub-viewport's canvas transform
        /// (world→viewport), integer display scale, and display origin. Result may be fractional
        /// (camera lerp) — fine for vector text; I1's strict integer rule only binds the blit.</summary>
        // No Vector2(Vector2I) ctor exists in GodotSharp — explicit component cast.
        public static Vector2 Project(Vector2 worldPos, Transform2D canvas, float scale, Vector2I origin)
            => canvas * worldPos * scale + new Vector2((float)origin.X, (float)origin.Y);

        /// <summary>True if the element's screen rect is culled: its interior is disjoint from the
        /// display rect's interior. Flush on the inside edge (e.g. element.Right == display.Right)
        /// is NOT culled — Rect2.Intersects requires interior overlap (probed).</summary>
        public static bool IsCulled(Rect2 element, Rect2 displayRect) => !element.Intersects(displayRect);
    }
}
