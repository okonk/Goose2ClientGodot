using System;

namespace Goose2Client.Character
{
    public static class CharacterAnchor
    {
        /// <summary>Vertical pixel offset for a slot sprite of the given frame height, so the
        /// feet line up at the character's tile-bottom origin (Unity CharacterAnimation.SetPosition).</summary>
        public static int OffsetY(int height) => -Math.Max((height - 48) / 2, 0) - 16;
    }
}
