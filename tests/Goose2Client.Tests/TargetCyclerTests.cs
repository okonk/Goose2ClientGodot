using System.Collections.Generic;
using Goose2Client;
using Xunit;

public class TargetCyclerTests
{
    private static TargetCandidate C(int id,int x,int y,CharacterType t)=>new(id,x,y,t);

    [Fact] public void FiltersToPlayersWhenTargetTypeIsPlayer()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Monster), C(2,6,5,CharacterType.Player) };
        Assert.Equal(2, TargetCycler.Next(all, null, (5,5), 100, SpellTargetType.Player, true, true)?.LoginId);
    }

    [Fact] public void FiltersOutPlayersWhenTargetTypeIsNpc()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Monster), C(2,6,5,CharacterType.Player) };
        Assert.Equal(1, TargetCycler.Next(all, null, (5,5), 100, SpellTargetType.NPC, true, true)?.LoginId);
    }

    [Fact] public void SkipsCandidatesOutsideViewWindow()
    {
        var all = new List<TargetCandidate>{ C(1,50,50,CharacterType.Monster) };
        Assert.Null(TargetCycler.Next(all, null, (5,5), 100, SpellTargetType.NPC, true, true));
    }

    [Fact] public void WrapsAroundWhenSearchingPastEnd()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Monster), C(2,7,5,CharacterType.Monster) };
        Assert.Equal(1, TargetCycler.Next(all, C(2,7,5,CharacterType.Monster), (6,5), 100,
            SpellTargetType.NPC, true, true)?.LoginId);
    }

    [Fact] public void NoFilteringForNpcPlayerType()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Player), C(2,6,5,CharacterType.Monster) };
        Assert.NotNull(TargetCycler.Next(all, null, (5,5), 100, SpellTargetType.NPCPlayer, true, true));
    }
}
