using Goose2Client.Character;
using Xunit;

namespace Goose2Client.Tests;

public class MovementInputTests
{
    [Fact]
    public void Resolve_no_keys_returns_null()
    {
        bool wasMovingVertical = false;
        var result = MovementInput.Resolve(false, false, false, false, ref wasMovingVertical);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(true, false, false, false, Direction.Up)]
    [InlineData(false, true, false, false, Direction.Down)]
    [InlineData(false, false, true, false, Direction.Left)]
    [InlineData(false, false, false, true, Direction.Right)]
    public void Resolve_single_key_returns_direction(bool up, bool down, bool left, bool right, Direction expected)
    {
        bool wasMovingVertical = false;
        var result = MovementInput.Resolve(up, down, left, right, ref wasMovingVertical);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Resolve_held_diagonal_alternates_axis()
    {
        bool wasMovingVertical = true;

        var d1 = MovementInput.Resolve(true, false, false, true, ref wasMovingVertical);
        Assert.Equal(Direction.Right, d1);
        Assert.False(wasMovingVertical);

        var d2 = MovementInput.Resolve(true, false, false, true, ref wasMovingVertical);
        Assert.Equal(Direction.Up, d2);
        Assert.True(wasMovingVertical);
    }

    /// <summary>Regression: when ProcessLocalInput uses a local copy of _wasMovingVertical
    /// for Resolve each frame, the direction must stay stable across repeated calls
    /// (simulating frames during hold delay / while moving). The persisted state only
    /// changes when the local copy is committed after a movement attempt.</summary>
    [Fact]
    public void Resolve_local_copy_pattern_stable_during_hold_then_alternates_per_attempt()
    {
        bool wasMovingVertical = true; // persisted state, starts vertical

        // --- Frame 1: diagonal keys held, standing, first frame ---
        bool next = wasMovingVertical;
        var dir1 = MovementInput.Resolve(true, false, false, true, ref next);
        Assert.Equal(Direction.Right, dir1); // wasMovingVertical=true → pick horizontal
        Assert.False(next);
        // Direction is stable (Right) — do NOT commit yet (hold delay not elapsed)

        // --- Frame 2: still holding diagonal, still in hold delay ---
        next = wasMovingVertical; // local copy from persisted state again
        var dir2 = MovementInput.Resolve(true, false, false, true, ref next);
        Assert.Equal(Direction.Right, dir2); // same as frame 1 — stable!
        // Still do NOT commit

        // --- Frame 3: hold delay elapsed, commit before attempting move ---
        next = wasMovingVertical;
        var dir3 = MovementInput.Resolve(true, false, false, true, ref next);
        Assert.Equal(Direction.Right, dir3);
        wasMovingVertical = next; // COMMIT: wasMovingVertical is now false
        Assert.False(wasMovingVertical);

        // --- Frame 4: character arrives, standing again, diagonal still held ---
        next = wasMovingVertical; // local copy from committed state
        var dir4 = MovementInput.Resolve(true, false, false, true, ref next);
        Assert.Equal(Direction.Up, dir4); // wasMovingVertical=false → pick vertical (alternated!)
        Assert.True(next);
        wasMovingVertical = next; // COMMIT
        Assert.True(wasMovingVertical);

        // --- Frame 5: next movement attempt ---
        next = wasMovingVertical;
        var dir5 = MovementInput.Resolve(true, false, false, true, ref next);
        Assert.Equal(Direction.Right, dir5); // alternated back to horizontal
        Assert.False(next);
        wasMovingVertical = next;
        Assert.False(wasMovingVertical);
    }

    /// <summary>Verify the local-copy pattern also works correctly for single-axis keys
    /// (no alternation needed, but wasMovingVertical should still update properly).</summary>
    [Fact]
    public void Resolve_local_copy_pattern_single_axis_stable()
    {
        bool wasMovingVertical = false;

        // Holding only Down across multiple "frames" with local copy
        for (int i = 0; i < 5; i++)
        {
            bool next = wasMovingVertical;
            var dir = MovementInput.Resolve(false, true, false, false, ref next);
            Assert.Equal(Direction.Down, dir);
            Assert.True(next);
            wasMovingVertical = next;
        }
        Assert.True(wasMovingVertical);
    }
}
