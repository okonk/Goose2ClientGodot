namespace Goose2Client.Character
{
    public static class CharacterMotion
    {
        public static string State(bool isMoving, string? lockedMotion, bool isMounted)
        {
            if (lockedMotion != null) return lockedMotion;
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

        /// <summary>After a tile step finishes: only return to idle when the next step was not
        /// chained in the same frame (key still held + valid tile).</summary>
        public static bool ShouldPlayIdleAfterStep(bool chainedNextStep) => !chainedNextStep;

        /// <summary>How much of a frame's step budget remains after covering
        /// <paramref name="distanceToTarget"/> pixels toward the current tile.</summary>
        public static float RemainingStepBudget(float stepBudget, float distanceToTarget)
        {
            if (stepBudget <= 0f) return 0f;
            if (distanceToTarget <= 0f) return stepBudget;
            float left = stepBudget - distanceToTarget;
            return left > 0f ? left : 0f;
        }
    }
}
