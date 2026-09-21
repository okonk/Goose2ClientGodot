using System.Collections.Generic;
using Goose2Client.Network;
using Xunit;

namespace Goose2Client.Network.Tests;

public class PacketInboxTests
{
    [Fact]
    public void Drain_StopsAtMaximumAndPreservesFifoAcrossFrames()
    {
        var inbox = new PacketInbox();
        var recorded = new List<string>();
        inbox.Enqueue("A");
        inbox.Enqueue("B");
        inbox.Enqueue("C");

        int firstDrain = inbox.Drain(2, recorded.Add);

        Assert.Equal(2, firstDrain);
        Assert.Equal(new[] { "A", "B" }, recorded);
        Assert.Equal(1, inbox.Count);

        int secondDrain = inbox.Drain(2, recorded.Add);

        Assert.Equal(1, secondDrain);
        Assert.Equal(new[] { "A", "B", "C" }, recorded);
        Assert.Equal(0, inbox.Count);
    }

    [Fact]
    public void Drain_StopsWhenBudgetExpires()
    {
        var inbox = new PacketInbox();
        var recorded = new List<string>();
        inbox.Enqueue("A");
        inbox.Enqueue("B");
        inbox.Enqueue("C");
        int checks = 0;

        int drained = inbox.Drain(10, () => checks++ < 2, recorded.Add);

        Assert.Equal(2, drained);
        Assert.Equal(new[] { "A", "B" }, recorded);
        Assert.Equal(1, inbox.Count);
    }

    [Fact]
    public void WhenEmpty_RunsAfterEarlierPacketsDrain()
    {
        var inbox = new PacketInbox();
        var recorded = new List<string>();
        inbox.Enqueue("A");
        inbox.Enqueue("B");
        inbox.WhenEmpty(() => recorded.Add("DISCONNECTED"));

        inbox.Drain(1, recorded.Add);

        Assert.Equal(new[] { "A" }, recorded);

        inbox.Drain(1, recorded.Add);

        Assert.Equal(new[] { "A", "B", "DISCONNECTED" }, recorded);
    }

    [Fact]
    public void Drain_WhenDispatchPauses_LeavesLaterPacketsAndCallbackPending()
    {
        var inbox = new PacketInbox();
        var recorded = new List<string>();
        bool paused = false;
        inbox.Enqueue("A");
        inbox.Enqueue("B");
        inbox.WhenEmpty(() => recorded.Add("DISCONNECTED"));

        inbox.Drain(10, () => !paused, packet =>
        {
            recorded.Add(packet);
            paused = true;
        });

        Assert.Equal(new[] { "A" }, recorded);
        Assert.Equal(1, inbox.Count);

        paused = false;
        inbox.Drain(10, () => !paused, recorded.Add);

        Assert.Equal(new[] { "A", "B", "DISCONNECTED" }, recorded);
    }

    [Fact]
    public void Drain_DoesNotRunCallbackWhenPacketArrivesAfterEmptyCheck()
    {
        var inbox = new PacketInbox();
        var recorded = new List<string>();
        bool injected = false;

        inbox.Drain(0, () =>
        {
            if (!injected)
            {
                injected = true;
                inbox.Enqueue("A");
                inbox.WhenEmpty(() => recorded.Add("DISCONNECTED"));
            }
            return true;
        }, recorded.Add);

        Assert.Empty(recorded);
        Assert.Equal(1, inbox.Count);

        inbox.Drain(1, recorded.Add);

        Assert.Equal(new[] { "A", "DISCONNECTED" }, recorded);
    }
}
