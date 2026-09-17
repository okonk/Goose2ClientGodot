using System.Collections.Generic;
using Goose2Client;
using Xunit;

public class TargetCyclerTests
{
    public TargetCyclerTests()
    {
        CurrentMapFlags.Value = new MapFlags(false, true, true);
    }

    private static TargetCandidate C(int id,int x,int y,CharacterType t)=>new(id,x,y,t);

    [Fact] public void FiltersToPlayersWhenTargetTypeIsPlayer()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Monster), C(2,6,5,CharacterType.Player) };
        Assert.Equal(2, TargetCycler.Next(all, null, (5,5), 0, 100, (10,8), SpellTargetType.Player, true, true)?.LoginId);
    }

    [Fact] public void FiltersOutPlayersWhenTargetTypeIsNpc()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Monster), C(2,6,5,CharacterType.Player) };
        Assert.Equal(1, TargetCycler.Next(all, null, (5,5), 0, 100, (10,8), SpellTargetType.NPC, true, true)?.LoginId);
    }

    [Fact] public void SkipsCandidatesOutsideViewWindow()
    {
        var all = new List<TargetCandidate>{ C(1,50,50,CharacterType.Monster) };
        Assert.Null(TargetCycler.Next(all, null, (5,5), 0, 100, (10,8), SpellTargetType.NPC, true, true));
    }

    [Fact] public void WrapsAroundWhenSearchingPastEnd()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Monster), C(2,7,5,CharacterType.Monster) };
        Assert.Equal(1, TargetCycler.Next(all, C(2,7,5,CharacterType.Monster), (6,5), 0, 100, (10,8),
            SpellTargetType.NPC, true, true)?.LoginId);
    }

    [Fact] public void PlayerTargetTypeIncludesPets()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Monster), C(2,6,5,CharacterType.Pet) };
        Assert.Equal(2, TargetCycler.Next(all, null, (5,5), 0, 100, (10,8), SpellTargetType.Player, true, true)?.LoginId);
    }

    [Fact] public void NpcTargetTypeExcludesPetsWhenPvpDisabled()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Monster), C(2,6,5,CharacterType.Pet) };
        Assert.Equal(1, TargetCycler.Next(all, null, (5,5), 0, 100, (10,8), SpellTargetType.NPC, true, true)?.LoginId);
    }

    [Fact] public void NpcTargetTypeExcludesPlayerSideEvenWhenPvpEnabled()
    {
        CurrentMapFlags.Value = new MapFlags(true, true, true);
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Monster), C(2,6,5,CharacterType.Player), C(3,7,5,CharacterType.Pet) };
        Assert.Equal(1, TargetCycler.Next(all, null, (5,5), 0, 100, (10,8), SpellTargetType.NPC, true, true)?.LoginId);
    }

    [Fact] public void NpcPlayerTargetTypeExcludesPlayerSideWhenPvpDisabled()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Player), C(2,6,5,CharacterType.Monster) };
        Assert.Equal(2, TargetCycler.Next(all, null, (5,5), 0, 100, (10,8), SpellTargetType.NPCPlayer, true, true)?.LoginId);
    }

    [Fact] public void NpcPlayerTargetTypeIncludesPlayerAndPetWhenPvpEnabled()
    {
        CurrentMapFlags.Value = new MapFlags(true, true, true);
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Monster), C(2,6,5,CharacterType.Player), C(3,7,5,CharacterType.Pet) };
        Assert.Equal(2, TargetCycler.Next(all, C(1,5,5,CharacterType.Monster), (5,5), 0, 100, (10,8), SpellTargetType.NPCPlayer, true, true)?.LoginId);
        Assert.Equal(3, TargetCycler.Next(all, C(2,6,5,CharacterType.Player), (5,5), 0, 100, (10,8), SpellTargetType.NPCPlayer, true, true)?.LoginId);
    }

    [Fact] public void BackwardCycleLandsOnLastCandidateWhenCurrentIsFilteredOut()
    {
        // Three NPC candidates sorted by position key (Y*mapWidth+X): login 1 at (0,0), 2 at (0,1), 3 at (0,2)
        var all = new List<TargetCandidate>
        {
            C(1, 0, 0, CharacterType.Monster),
            C(2, 0, 1, CharacterType.Monster),
            C(3, 0, 2, CharacterType.Monster)
        };
        // Current target is a Player (login 99) — filtered out when cycling NPCs
        var current = C(99, 0, 0, CharacterType.Player);
        var result = TargetCycler.Next(all, current, (1, 1), 0, 100, (10,8), SpellTargetType.NPC, true, false);
        // Backward from no-match should wrap to last candidate (login 3), not second-to-last (login 2)
        Assert.Equal(3, result?.LoginId);
    }

    [Fact] public void ForwardFromStaleTargetSearchesFromStalePosition()
    {
        var all = new List<TargetCandidate>
        {
            C(1, 0, 0, CharacterType.Monster),
            C(2, 0, 2, CharacterType.Monster),
            C(3, 0, 4, CharacterType.Monster)
        };
        // Stale target (Player, filtered out) at (0,3) — key 300, between candidates 2 (200) and 3 (400)
        var current = C(99, 0, 3, CharacterType.Player);
        // Forward must pick the nearest candidate below the stale position (login 3), not the first (login 1)
        Assert.Equal(3, TargetCycler.Next(all, current, (1, 1), 0, 100, (10,8), SpellTargetType.NPC, true, true)?.LoginId);
    }

    [Fact] public void BackwardFromStaleTargetSearchesFromStalePosition()
    {
        var all = new List<TargetCandidate>
        {
            C(1, 0, 0, CharacterType.Monster),
            C(2, 0, 2, CharacterType.Monster),
            C(3, 0, 4, CharacterType.Monster)
        };
        var current = C(99, 0, 3, CharacterType.Player);
        // Backward must pick the nearest candidate above the stale position (login 2), not the last (login 3)
        Assert.Equal(2, TargetCycler.Next(all, current, (1, 1), 0, 100, (10,8), SpellTargetType.NPC, true, false)?.LoginId);
    }

    [Fact] public void ForwardFromStaleTargetWrapsToFirstWhenNothingBelow()
    {
        var all = new List<TargetCandidate>
        {
            C(1, 0, 0, CharacterType.Monster),
            C(2, 0, 2, CharacterType.Monster),
            C(3, 0, 4, CharacterType.Monster)
        };
        // Stale target at (0,5) — key 500, below every candidate
        var current = C(99, 0, 5, CharacterType.Player);
        Assert.Equal(1, TargetCycler.Next(all, current, (1, 1), 0, 100, (10,8), SpellTargetType.NPC, true, true)?.LoginId);
    }

    [Fact] public void LocalPlayerIsNeverExcludedByTypeFiltering()
    {
        // Local player (login 7) stands next to a monster; NPC-target spell with filtering on
        var all = new List<TargetCandidate>
        {
            C(7, 5, 5, CharacterType.Player),
            C(1, 6, 5, CharacterType.Monster)
        };
        // Forward from no target lands on the local player (first by position key)
        Assert.Equal(7, TargetCycler.Next(all, null, (5,5), 7, 100, (10,8), SpellTargetType.NPC, true, true)?.LoginId);
        // Cycling forward from the local player reaches the monster, and backward wraps back to it
        Assert.Equal(1, TargetCycler.Next(all, C(7,5,5,CharacterType.Player), (5,5), 7, 100, (10,8), SpellTargetType.NPC, true, true)?.LoginId);
        Assert.Equal(7, TargetCycler.Next(all, C(1,6,5,CharacterType.Monster), (5,5), 7, 100, (10,8), SpellTargetType.NPC, true, false)?.LoginId);
    }

    [Fact] public void OtherPlayersAreStillExcludedWhenPvpDisabled()
    {
        var all = new List<TargetCandidate>
        {
            C(7, 5, 5, CharacterType.Player),
            C(2, 6, 5, CharacterType.Player)
        };
        Assert.Equal(7, TargetCycler.Next(all, null, (5,5), 7, 100, (10,8), SpellTargetType.NPCPlayer, true, true)?.LoginId);
    }
}
