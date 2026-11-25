using System.Collections.Generic;
using System.Linq;

namespace Goose2Client
{
    public static class SpellPreview
    {
        public static (int X, int Y) Anchor(SpellInfo spell, (int X, int Y)? hover, (int X, int Y) target)
            => spell.GroundTargetable && hover is { } h ? h : target;

        public static List<(int X, int Y)> Tiles(SpellInfo spell, (int X, int Y) anchor, Direction facing,
            (int Width, int Height) mapSize)
        {
            if (!SpellShapes.HasPreview(spell.Shape)) return new();

            return SpellShapes.Tiles(spell.Shape, spell.ShapeSize, anchor.X, anchor.Y, facing)
                .Where(t => t.X >= 0 && t.Y >= 0 && t.X < mapSize.Width && t.Y < mapSize.Height)
                .Distinct()
                .ToList();
        }
    }
}
