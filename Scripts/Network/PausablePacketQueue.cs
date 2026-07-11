using System;
using System.Collections.Generic;

namespace Goose2Client.Network
{
    /// <summary>
    /// FIFO queue that buffers packets while paused and drains them on unpause.
    /// Pure logic class — no Godot dependencies — so it is unit-testable.
    /// </summary>
    public class PausablePacketQueue
    {
        private readonly Queue<string> _queued = new();
        private readonly Func<bool> _isPaused;
        private readonly Action<string> _dispatch;

        public PausablePacketQueue(Func<bool> isPaused, Action<string> dispatch)
        {
            _isPaused = isPaused;
            _dispatch = dispatch;
        }

        /// <summary>Number of packets currently buffered (paused) but not yet dispatched.</summary>
        public int Count => _queued.Count;

        /// <summary>
        /// Enqueue the packet if paused; otherwise dispatch it immediately.
        /// </summary>
        public void Handle(string packet)
        {
            if (_isPaused())
            {
                _queued.Enqueue(packet);
                return;
            }
            _dispatch(packet);
        }

        /// <summary>
        /// Drains queued packets in FIFO order until the queue is empty or re-paused.
        /// <para>
        /// If a dispatched packet synchronously re-pauses the queue (e.g. a map change
        /// that triggers a context reload), Drain stops immediately. Remaining packets
        /// stay buffered for the next call when the queue is unpaused again.
        /// </para>
        /// <para>
        /// This matches Unity client behavior where mid-batch re-pauses prevent
        /// stale packets from processing in the wrong map context.
        /// </para>
        /// <para>
        /// Calling Drain on an empty queue is a no-op.
        /// </para>
        /// </summary>
        public void Drain()
        {
            while (_queued.Count > 0 && !_isPaused())
                _dispatch(_queued.Dequeue());
        }
    }
}
