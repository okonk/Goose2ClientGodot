namespace Goose2Client.Character
{
    public static class CharacterMotion
    {
        public static string State(bool isMoving, bool attackLocked, bool isMounted)
        {
            if (attackLocked) return "attack";
            if (isMounted) return isMoving ? "mounted-walk" : "mounted-idle";
            return isMoving ? "walk" : "idle";
        }

        /// <summary>Tile-to-tile travel speed in px/s. Unity used MoveTowards at 1000/MoveSpeed
        /// world units/s (1 unit = 1 tile = 32 px).</summary>
        public static float PixelsPerSecond(int moveSpeed)
        {
            int safe = moveSpeed <= 0 ? 250 : moveSpeed;   // guard against div-by-zero / bad data
            return 32f * (1000f / safe);
        }
    }
}
