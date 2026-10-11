using Goose2Client.Overlays;
using Xunit;

public class BattleTextManagerTests
{
    private static readonly BattleTextMergeKey Damage = new(BattleTextFamily.Red, "");
    private static readonly BattleTextMergeKey Heal = new(BattleTextFamily.Green, "");
    private static readonly BattleTextMergeKey Xp = new(BattleTextFamily.Yellow, "XP");

    [Fact]
    public void FirstPacket_StartsAccumulator()
    {
        var m = new BattleTextManager<string>();
        var r = m.OnPacket(Damage, -12);
        Assert.Equal(BattleTextMergeAction.Start, r.Action);
        Assert.Equal("-12", r.DisplayText);
    }

    [Fact]
    public void LaterPackets_AccumulateAndFormat()
    {
        var m = new BattleTextManager<string>();
        m.OnPacket(Damage, -12);
        Assert.Equal(BattleTextMergeAction.Accumulate, m.OnPacket(Damage, -25).Action);
        Assert.Equal("-37", m.OnPacket(Damage, 0).DisplayText);
        Assert.Equal("-1,237", m.OnPacket(Damage, -1200).DisplayText);
    }

    [Fact]
    public void Keys_AreIsolated()
    {
        var m = new BattleTextManager<string>();
        m.OnPacket(Damage, -10);
        Assert.Equal(BattleTextMergeAction.Start, m.OnPacket(Heal, 5).Action);
        Assert.Equal(BattleTextMergeAction.Start, m.OnPacket(Xp, 100).Action);
        Assert.Equal("+150 XP", m.OnPacket(Xp, 50).DisplayText);
    }

    [Fact]
    public void Gap_Finalizes_AndNextPacketStartsFresh()
    {
        var m = new BattleTextManager<string>();
        m.OnPacket(Damage, -10);
        m.Attach(Damage, "line");
        Assert.Empty(m.Tick(0.9));
        var finalized = m.Tick(0.15);
        Assert.Single(finalized);
        Assert.Equal(Damage, finalized[0].Key);
        Assert.Equal("line", finalized[0].Handle);
        Assert.Equal(BattleTextMergeAction.Start, m.OnPacket(Damage, -7).Action);
    }

    [Fact]
    public void Cap_FinalizesEvenWithoutGap()
    {
        var m = new BattleTextManager<string>();
        m.OnPacket(Damage, -10);
        m.Attach(Damage, "line");
        for (int i = 1; i <= 8; i++)
        {
            m.OnPacket(Damage, -1);
            var finalized = m.Tick(0.25);
            if (finalized.Count == 0) continue;
            Assert.Equal(8, i);
            Assert.True(m.FinalizedByCap);
            return;
        }
        Assert.Fail("cap never finalized");
    }

    [Fact]
    public void Cancel_DropsRecordWithoutFinalizeSignal()
    {
        var m = new BattleTextManager<string>();
        m.OnPacket(Damage, -10);
        m.Attach(Damage, "line");
        m.Cancel(Damage);
        Assert.Empty(m.Tick(5.0));
        Assert.Equal(BattleTextMergeAction.Start, m.OnPacket(Damage, -3).Action);
    }

    [Fact]
    public void Attach_UnknownKey_IsNoOp()
    {
        var m = new BattleTextManager<string>();
        m.Attach(Heal, "x");
        Assert.False(m.TryGetHandle(Heal, out _));
    }

    [Fact]
    public void TryGetHandle_UnknownKey_ReturnsFalse()
    {
        var m = new BattleTextManager<string>();
        Assert.False(m.TryGetHandle(Xp, out _));
    }

    [Fact]
    public void Attach_TryGetHandle_RoundTrips()
    {
        var m = new BattleTextManager<string>();
        m.OnPacket(Damage, -10);
        Assert.False(m.TryGetHandle(Damage, out _));
        m.Attach(Damage, "line");
        Assert.True(m.TryGetHandle(Damage, out var h));
        Assert.Equal("line", h);
    }
}
