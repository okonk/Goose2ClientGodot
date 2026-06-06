using System.Collections.Generic;
using Goose2Client.Network;
using Xunit;

namespace Goose2Client.Network.Tests
{
    public class PauseQueueTests
    {
        [Fact]
        public void Handle_WhilePaused_EnqueuesWithoutDispatching()
        {
            // Arrange: queue with a fake pause flag and recording dispatch
            bool paused = false;
            var recorded = new List<string>();
            var queue = new PausablePacketQueue(() => paused, s => recorded.Add(s));

            // Act: pause, then send packets
            paused = true;
            queue.Handle("A");
            queue.Handle("B");
            queue.Handle("C");

            // Assert: nothing dispatched while paused (regression against drop-AND-dispatch bugs)
            Assert.Empty(recorded);
            Assert.Equal(3, queue.Count);
        }

        [Fact]
        public void Drain_DeliversQueuedPacketsInFifoOrder()
        {
            // Arrange
            bool paused = false;
            var recorded = new List<string>();
            var queue = new PausablePacketQueue(() => paused, s => recorded.Add(s));

            paused = true;
            queue.Handle("A");
            queue.Handle("B");
            queue.Handle("C");

            // Act: unpause, then drain
            paused = false;
            queue.Drain();

            // Assert: FIFO order preserved
            Assert.Equal(new[] { "A", "B", "C" }, recorded);
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void Handle_AfterUnpause_DispatchesInline()
        {
            // Arrange
            bool paused = false;
            var recorded = new List<string>();
            var queue = new PausablePacketQueue(() => paused, s => recorded.Add(s));

            // Queue some packets while paused, drain them
            paused = true;
            queue.Handle("A");
            queue.Handle("B");
            paused = false;
            queue.Drain();

            // Act: send a new packet while unpaused
            queue.Handle("D");

            // Assert: dispatched inline immediately
            Assert.Equal(new[] { "A", "B", "D" }, recorded);
        }

        [Fact]
        public void Handle_WhileNeverPaused_DispatchesInlineImmediately()
        {
            // Arrange
            bool paused = false;
            var recorded = new List<string>();
            var queue = new PausablePacketQueue(() => paused, s => recorded.Add(s));

            // Act: send packets without ever pausing
            queue.Handle("X");
            queue.Handle("Y");

            // Assert: both dispatched inline
            Assert.Equal(new[] { "X", "Y" }, recorded);
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void Drain_EmptyQueue_IsNoop()
        {
            // Arrange
            bool paused = false;
            var recorded = new List<string>();
            var queue = new PausablePacketQueue(() => paused, s => recorded.Add(s));

            // Act: drain with nothing queued
            queue.Drain();

            // Assert: no dispatches occurred
            Assert.Empty(recorded);
        }

        [Fact]
        public void FullCycle_PauseQueueUnpauseDrainThenInline()
        {
            // Arrange: adversarial regression test — the exact scenario that caused desyncs
            bool paused = false;
            var recorded = new List<string>();
            var queue = new PausablePacketQueue(() => paused, s => recorded.Add(s));

            // Send one packet before pause (inline)
            queue.Handle("P0");

            // Pause and buffer
            paused = true;
            queue.Handle("P1");
            queue.Handle("P2");
            queue.Handle("P3");

            // Unpause and drain — queued packets must come BEFORE any new inline packet
            paused = false;
            queue.Drain();
            queue.Handle("P4"); // arrives after drain, should dispatch inline

            // Assert: full FIFO order preserved across pause boundary
            Assert.Equal(new[] { "P0", "P1", "P2", "P3", "P4" }, recorded);
        }
    }
}
