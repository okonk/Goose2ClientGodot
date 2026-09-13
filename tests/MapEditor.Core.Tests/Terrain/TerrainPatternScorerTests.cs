using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class TerrainPatternScorerTests
{
    private static readonly Guid[] Ids =
    [
        new("00000000-0000-0000-0000-000000000001"),
        new("01234567-89ab-cdef-0123-456789abcdef"),
        new("ffffffff-ffff-ffff-ffff-ffffffffffff"),
        new("abcd1234-5678-9abc-def0-123456789abc")
    ];

    private static Guid? Id(int index) => index == 0 ? null : Ids[index - 1];

    private static TerrainPattern AllPeers(params int[] peers)
    {
        var pattern = new TerrainPattern();
        pattern = pattern with { North = Id(peers[0]) };
        pattern = pattern with { East = Id(peers[1]) };
        pattern = pattern with { South = Id(peers[2]) };
        pattern = pattern with { West = Id(peers[3]) };
        pattern = pattern with { NorthEast = Id(peers[4]) };
        pattern = pattern with { SouthEast = Id(peers[5]) };
        pattern = pattern with { SouthWest = Id(peers[6]) };
        pattern = pattern with { NorthWest = Id(peers[7]) };
        return pattern;
    }

    private static TerrainPatternCandidateGroup Group(TerrainPattern pattern, params (int Sheet, int Graphic)[] variants)
        => new(pattern, variants.Select(v => new TerrainGraphicDefinition(new TerrainGraphicReference(v.Sheet, v.Graphic), pattern)).ToList());

    [Theory]
    [InlineData(1, 1, 2, 2)]
    [InlineData(0, 0, 2, 2)]
    [InlineData(1, 0, 2, 0)]
    [InlineData(0, 1, 2, -2)]
    [InlineData(2, 3, 2, -2)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(1, 0, 1, 0)]
    [InlineData(0, 1, 1, -1)]
    [InlineData(2, 3, 1, -1)]
    public void ScorePeer_ExactAddsNeutralIsZeroMismatchSubtracts(int desired, int candidate, int weight, int expected)
    {
        Assert.Equal(expected, TerrainPatternScorer.ScorePeer(Id(desired), Id(candidate), weight));
    }

    [Fact]
    public void Score_SidesWeighTwoCornersWeighOne()
    {
        var desired = AllPeers(0, 0, 0, 0, 0, 0, 0, 0);
        var allNull = AllPeers(0, 0, 0, 0, 0, 0, 0, 0);

        Assert.Equal(12, TerrainPatternScorer.Score(desired, allNull));
        Assert.Equal(8, TerrainPatternScorer.Score(desired, AllPeers(1, 0, 0, 0, 0, 0, 0, 0)));
        Assert.Equal(8, TerrainPatternScorer.Score(desired, AllPeers(0, 1, 0, 0, 0, 0, 0, 0)));
        Assert.Equal(8, TerrainPatternScorer.Score(desired, AllPeers(0, 0, 1, 0, 0, 0, 0, 0)));
        Assert.Equal(8, TerrainPatternScorer.Score(desired, AllPeers(0, 0, 0, 1, 0, 0, 0, 0)));
        Assert.Equal(10, TerrainPatternScorer.Score(desired, AllPeers(0, 0, 0, 0, 1, 0, 0, 0)));
        Assert.Equal(10, TerrainPatternScorer.Score(desired, AllPeers(0, 0, 0, 0, 0, 1, 0, 0)));
        Assert.Equal(10, TerrainPatternScorer.Score(desired, AllPeers(0, 0, 0, 0, 0, 0, 1, 0)));
        Assert.Equal(10, TerrainPatternScorer.Score(desired, AllPeers(0, 0, 0, 0, 0, 0, 0, 1)));
    }

    [Fact]
    public void Score_RangeIsMinusTwelveToTwelve()
    {
        var desired = AllPeers(1, 2, 3, 4, 1, 2, 3, 4);

        Assert.Equal(12, TerrainPatternScorer.Score(desired, AllPeers(1, 2, 3, 4, 1, 2, 3, 4)));
        Assert.Equal(-12, TerrainPatternScorer.Score(desired, AllPeers(2, 3, 4, 1, 2, 3, 4, 1)));
    }

    [Fact]
    public void Score_SideMismatchOutweighsCornerMatch()
    {
        var desired = AllPeers(1, 1, 1, 1, 1, 1, 1, 1);
        var sideMismatch = AllPeers(2, 1, 1, 1, 1, 1, 1, 1);
        var sideMissing = AllPeers(0, 1, 1, 1, 1, 1, 1, 1);

        Assert.True(TerrainPatternScorer.Score(desired, sideMissing) > TerrainPatternScorer.Score(desired, sideMismatch));
    }

    [Fact]
    public void Score_ExactEightPeerPatternOutranksEveryIncompleteAlternative()
    {
        var desired = AllPeers(1, 2, 3, 4, 1, 2, 3, 4);
        var alternatives = new List<TerrainPattern>();
        for (var peer = 0; peer < 8; peer++)
        {
            var basePeers = new[] { 1, 2, 3, 4, 1, 2, 3, 4 };
            var missing = (int[])basePeers.Clone();
            missing[peer] = 0;
            alternatives.Add(AllPeers(missing));
            var wrong = (int[])basePeers.Clone();
            wrong[peer] = basePeers[peer] % Ids.Length + 1;
            alternatives.Add(AllPeers(wrong));
        }

        var exactScore = TerrainPatternScorer.Score(desired, desired);
        Assert.Equal(12, exactScore);
        Assert.All(alternatives, alternative => Assert.True(TerrainPatternScorer.Score(desired, alternative) < exactScore));
    }

    [Fact]
    public void Select_ShuffledCandidatesProduceIdenticalSelection()
    {
        var desired = AllPeers(1, 2, 3, 4, 1, 2, 3, 4);
        desired = desired with { Center = Ids[0] };
        var candidates = new List<TerrainPatternCandidateGroup>
        {
            Group(AllPeers(1, 2, 3, 4, 1, 2, 3, 4), (0, 1), (0, 2)),
            Group(AllPeers(1, 2, 3, 4, 1, 2, 3, 0), (1, 1)),
            Group(AllPeers(1, 0, 3, 4, 1, 2, 3, 4), (1, 2)),
            Group(AllPeers(2, 2, 3, 4, 1, 2, 3, 4), (2, 1), (2, 2), (2, 3))
        };

        var baseline = TerrainPatternScorer.Select(desired, 5, -3, candidates);
        var shuffled = new Random(1234);
        for (var i = candidates.Count - 1; i > 0; i--)
        {
            var j = shuffled.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        var selection = TerrainPatternScorer.Select(desired, 5, -3, candidates);

        Assert.Equal(baseline.Pattern, selection.Pattern);
        Assert.Equal(baseline.Variant, selection.Variant);
        Assert.Equal(baseline.Score, selection.Score);
        Assert.Equal(baseline.TiedPatternCount, selection.TiedPatternCount);
        Assert.Equal(baseline.VariantCount, selection.VariantCount);
    }

    [Fact]
    public void Select_DuplicatePatternsAndVariantsDoNotMultiplyOptions()
    {
        var desired = AllPeers(1, 2, 3, 4, 1, 2, 3, 4);
        desired = desired with { Center = Ids[0] };
        var pattern = AllPeers(1, 2, 3, 4, 1, 2, 3, 4);
        var group = Group(pattern, (0, 1), (0, 2), (0, 1), (0, 2));
        var candidates = new List<TerrainPatternCandidateGroup> { group, group, group };

        var selection = TerrainPatternScorer.Select(desired, 0, 0, candidates);

        Assert.Equal(1, selection.TiedPatternCount);
        Assert.Equal(2, selection.VariantCount);
        Assert.Equal(pattern, selection.Pattern);
        Assert.Equal(12, selection.Score);
    }

    [Fact]
    public void Select_DuplicatePatternsWithDisjointVariantsMergeAcrossGroupOrderings()
    {
        var desired = AllPeers(1, 2, 3, 4, 1, 2, 3, 4);
        desired = desired with { Center = Ids[0] };
        var pattern = AllPeers(1, 2, 3, 4, 1, 2, 3, 4);
        var candidates = new List<TerrainPatternCandidateGroup>
        {
            Group(pattern, (0, 1)),
            Group(pattern, (0, 2)),
            Group(pattern, (0, 3))
        };

        var baseline = TerrainPatternScorer.Select(desired, 4, 4, candidates);
        Assert.Equal(1, baseline.TiedPatternCount);
        Assert.Equal(3, baseline.VariantCount);

        var shuffled = new Random(77);
        for (var i = 0; i < 5; i++)
        {
            for (var j = candidates.Count - 1; j > 0; j--)
            {
                var k = shuffled.Next(j + 1);
                (candidates[j], candidates[k]) = (candidates[k], candidates[j]);
            }

            Assert.Equal(baseline, TerrainPatternScorer.Select(desired, 4, 4, candidates));
        }

        var emptyFirst = new List<TerrainPatternCandidateGroup>
        {
            Group(pattern),
            Group(pattern, (0, 2))
        };

        var resolved = TerrainPatternScorer.Select(desired, 4, 4, emptyFirst);
        Assert.Equal(pattern, resolved.Pattern);
        Assert.Equal(1, resolved.VariantCount);
    }

    [Fact]
    public void Select_HighestScoringPatternWinsOverTies()
    {
        var desired = AllPeers(1, 2, 3, 4, 1, 2, 3, 4);
        desired = desired with { Center = Ids[0] };
        var candidates = new List<TerrainPatternCandidateGroup>
        {
            Group(AllPeers(1, 2, 3, 4, 1, 2, 3, 0), (0, 1)),
            Group(AllPeers(1, 2, 3, 4, 1, 2, 3, 4), (0, 2)),
            Group(AllPeers(0, 2, 3, 4, 1, 2, 3, 4), (0, 3))
        };

        var selection = TerrainPatternScorer.Select(desired, 0, 0, candidates);

        Assert.Equal(AllPeers(1, 2, 3, 4, 1, 2, 3, 4), selection.Pattern);
        Assert.Equal(12, selection.Score);
        Assert.Equal(1, selection.TiedPatternCount);
    }

    [Fact]
    public void Select_IsDeterministicAcrossRepeatedCalls()
    {
        var desired = AllPeers(0, 0, 0, 0, 0, 0, 0, 0);
        desired = desired with { Center = Ids[0] };
        var candidates = new List<TerrainPatternCandidateGroup>
        {
            Group(AllPeers(1, 0, 0, 0, 0, 0, 0, 0), (0, 1), (0, 2)),
            Group(AllPeers(0, 1, 0, 0, 0, 0, 0, 0), (1, 1)),
            Group(AllPeers(0, 0, 0, 1, 0, 0, 0, 0), (1, 2))
        };

        var first = TerrainPatternScorer.Select(desired, -7, 11, candidates);

        for (var i = 0; i < 10; i++)
        {
            var next = TerrainPatternScorer.Select(desired, -7, 11, candidates);
            Assert.Equal(first, next);
        }

        Assert.Equal(3, first.TiedPatternCount);
        Assert.Equal(8, first.Score);
    }

    [Fact]
    public void Select_RejectsEmptyCandidates()
    {
        var desired = AllPeers(1, 2, 3, 4, 1, 2, 3, 4) with { Center = Ids[0] };

        Assert.Throws<ArgumentException>(() => TerrainPatternScorer.Select(desired, 0, 0, Array.Empty<TerrainPatternCandidateGroup>()));
    }

    [Fact]
    public void Select_RejectsDesiredPatternWithoutCenter()
    {
        var candidates = new List<TerrainPatternCandidateGroup> { Group(AllPeers(1, 2, 3, 4, 1, 2, 3, 4), (0, 1)) };

        Assert.Throws<ArgumentException>(() => TerrainPatternScorer.Select(AllPeers(1, 2, 3, 4, 1, 2, 3, 4), 0, 0, candidates));
    }

    [Fact]
    public void Select_RejectsGroupWithoutVariants()
    {
        var desired = AllPeers(1, 2, 3, 4, 1, 2, 3, 4) with { Center = Ids[0] };
        var candidates = new List<TerrainPatternCandidateGroup> { Group(AllPeers(1, 2, 3, 4, 1, 2, 3, 4)) };

        Assert.Throws<InvalidOperationException>(() => TerrainPatternScorer.Select(desired, 0, 0, candidates));
    }
}
