namespace Goose2Client.Character
{
    /// <summary>Pure resolver for movement key states → single cardinal direction.
    /// No keys → null. Single key → respective direction.
    /// Horizontal+vertical → staircase: pick the axis NOT used last;
    /// horizontal prefers left over right; vertical prefers up over down.
    /// Updates <paramref name="wasMovingVertical"/> to reflect the returned axis.</summary>
    public static class MovementInput
    {
        /// <summary>Commit the axis selected by <see cref="Resolve"/> only when its move succeeds.
        /// Returns whether the caller should perform/send the move.</summary>
        public static bool TryCommitMoveAxis(bool isValidMove, bool nextWasMovingVertical,
                                             ref bool wasMovingVertical)
        {
            if (!isValidMove) return false;
            wasMovingVertical = nextWasMovingVertical;
            return true;
        }

        public static Direction? Resolve(bool up, bool down, bool left, bool right, ref bool wasMovingVertical)
        {
            bool hasVertical = up || down;
            bool hasHorizontal = left || right;

            if (!hasVertical && !hasHorizontal)
                return null;

            if (hasVertical && !hasHorizontal)
            {
                wasMovingVertical = true;
                return up ? Direction.Up : Direction.Down;
            }

            if (hasHorizontal && !hasVertical)
            {
                wasMovingVertical = false;
                return left ? Direction.Left : Direction.Right;
            }

            // Both axes held — alternate: pick axis NOT used last
            if (wasMovingVertical)
            {
                wasMovingVertical = false;
                return left ? Direction.Left : Direction.Right;
            }

            wasMovingVertical = true;
            return up ? Direction.Up : Direction.Down;
        }
    }
}
