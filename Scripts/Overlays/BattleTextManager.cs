using System.Collections.Generic;

namespace Goose2Client.Overlays
{
    public enum BattleTextMergeAction
    {
        Start,
        Accumulate
    }

    public sealed record BattleTextMergeResult(BattleTextMergeAction Action, string DisplayText);

    public sealed class BattleTextManager<THandle>
    {
        public const double GapSeconds = 1.0;
        public const double CapSeconds = 1.0;

        private sealed class Record
        {
            public long Total;
            public double Age;
            public double SinceLastHit;
            public THandle Handle = default!;
        }

        private readonly Dictionary<BattleTextMergeKey, Record> _open = new();
        private bool _finalizedByCap;

        public bool FinalizedByCap => _finalizedByCap;

        public BattleTextMergeResult OnPacket(BattleTextMergeKey key, long value)
        {
            if (_open.TryGetValue(key, out Record? r))
            {
                r.Total += value;
                r.SinceLastHit = 0;
                return new BattleTextMergeResult(BattleTextMergeAction.Accumulate, BattleTextMergeRule.Format(key, r.Total));
            }

            _open[key] = new Record { Total = value };
            return new BattleTextMergeResult(BattleTextMergeAction.Start, BattleTextMergeRule.Format(key, value));
        }

        public void Attach(BattleTextMergeKey key, THandle handle)
        {
            if (_open.TryGetValue(key, out Record? r))
            {
                r.Handle = handle;
            }
        }

        public void Cancel(BattleTextMergeKey key)
        {
            _open.Remove(key);
        }

        public bool TryGetHandle(BattleTextMergeKey key, out THandle handle)
        {
            handle = default!;
            if (!_open.TryGetValue(key, out Record? r))
            {
                return false;
            }
            if (EqualityComparer<THandle>.Default.Equals(r.Handle, default))
            {
                return false;
            }
            handle = r.Handle;
            return true;
        }

        public IReadOnlyList<(BattleTextMergeKey Key, THandle Handle)> Tick(double delta)
        {
            _finalizedByCap = false;
            var finalized = new List<(BattleTextMergeKey, THandle)>();
            foreach (var (key, r) in _open)
            {
                r.Age += delta;
                r.SinceLastHit += delta;
                if (r.SinceLastHit >= GapSeconds || r.Age >= CapSeconds)
                {
                    if (r.Age >= CapSeconds)
                    {
                        _finalizedByCap = true;
                    }
                    finalized.Add((key, r.Handle));
                }
            }
            foreach (var (key, _) in finalized)
            {
                _open.Remove(key);
            }
            return finalized;
        }
    }
}
