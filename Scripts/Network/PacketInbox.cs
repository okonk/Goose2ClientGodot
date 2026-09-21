using System;
using System.Collections.Concurrent;

namespace Goose2Client.Network
{
    public sealed class PacketInbox
    {
        private readonly ConcurrentQueue<string> _packets = new();
        private readonly ConcurrentQueue<Action> _whenEmpty = new();
        private readonly object _enqueueLock = new();

        public int Count => _packets.Count;

        public void Enqueue(string packet)
        {
            lock (_enqueueLock)
                _packets.Enqueue(packet);
        }

        public void WhenEmpty(Action action)
        {
            lock (_enqueueLock)
                _whenEmpty.Enqueue(action);
        }

        public int Drain(int maximum, Action<string> dispatch)
            => Drain(maximum, () => true, dispatch);

        public int Drain(int maximum, Func<bool> hasBudget, Action<string> dispatch)
        {
            int drained = 0;
            while (drained < maximum && hasBudget() && _packets.TryDequeue(out string? packet))
            {
                dispatch(packet);
                drained++;
            }
            while (hasBudget())
            {
                Action? action;
                lock (_enqueueLock)
                {
                    if (!_packets.IsEmpty || !_whenEmpty.TryDequeue(out action))
                        break;
                }
                action();
            }
            return drained;
        }

        public void Clear()
        {
            lock (_enqueueLock)
            {
                _packets.Clear();
                _whenEmpty.Clear();
            }
        }
    }
}
