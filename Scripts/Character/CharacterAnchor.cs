using System;

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
    }
}
