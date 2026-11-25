namespace Goose2Client
{
    public static class GroundTargetRule
    {
        public static bool CanCastAt(SpellInfo spell, (int X, int Y) player, (int X, int Y) tile,
            (int X, int Y) viewRange, (int Width, int Height) mapSize)
            => spell.GroundTargetable &&
               tile.X >= 0 && tile.Y >= 0 && tile.X < mapSize.Width && tile.Y < mapSize.Height &&
               System.Math.Abs(tile.X - player.X) <= viewRange.X &&
               System.Math.Abs(tile.Y - player.Y) <= viewRange.Y;
    }
}
