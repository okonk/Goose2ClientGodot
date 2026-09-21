using System;
using Godot;

namespace Goose2Client.Character
{
    public static class CharacterAnchor
    {
        /// <summary>Vertical pixel offset (Godot, y-down) for a Centered slot sprite of the given
        /// frame height, so the feet land at the Character node's tile-bottom origin.
        /// Derived from Unity CharacterAnimation.SetPosition (yOffset = -max((h-48)/2,0)-16, applied
        /// at tile-center with a bottom-pivot sprite) converted to this port's tile-bottom origin +
        /// center pivot: collapses to -24 for every h >= 48 (the standard 48px sprite's feet sit on
        /// the tile, taller sprites overhang downward), and -h/2 for short sprites.</summary>
        public static int OffsetY(int height) => Math.Max((height - 48) / 2, 0) - height / 2;

        public static Vector2 SpriteOffset(int height, Vector2 frameSize) =>
            new(frameSize.X % 2f * 0.5f, OffsetY(height) + frameSize.Y % 2f * 0.5f);

        public static int MountedHairYOffset(int graphicId, string animation, int frame)
        {
            // Hair 33's mounted frames bob on a different cadence from the rider body art.
            if (graphicId != 33) return 0;
            return animation switch
            {
                "mounted-walk-left" or "mounted-walk-right" when frame is 2 or 4 => 2,
                "mounted-walk-up" when frame == 1 => -3,
                "mounted-walk-up" when frame is 2 or 3 or 4 => -1,
                _ => 0,
            };
        }
    }
}
