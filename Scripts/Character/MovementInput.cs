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

        /// <summary>Bitmask of the held direction keys, so one combination compares equal to itself
        /// whatever order the keys were pressed in.</summary>
        public static int KeyMask(bool up, bool down, bool left, bool right)
            => (up ? 1 : 0) | (down ? 2 : 0) | (left ? 4 : 0) | (right ? 8 : 0);

        /// <summary>Age of the currently held key combination, in seconds: a change restarts it at
        /// zero, so only a combination the player has held for the move-start delay may take a step.
        /// Alternating diagonals keep the same combination, so they never pay the delay twice.</summary>
        public static double HeldTime(int mask, int previousMask, double previous, double delta)
            => mask == previousMask ? previous + delta : 0;

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
