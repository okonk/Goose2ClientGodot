using System;

namespace Goose2Client.Diagnostics
{
    public enum StallEventKind
    {
        Started,
        Ongoing,
        Recovered
    }

    public readonly record struct StallEvent(
        StallEventKind Kind,
        TimeSpan Duration,
        string Activity,
        int InboxCount);

    public sealed class MainThreadStallDetector
    {
        private readonly object _sync = new();
        private readonly TimeSpan _threshold;
        private readonly TimeSpan _reportInterval;
        private TimeSpan _lastHeartbeat;
        private TimeSpan _stallStart;
        private TimeSpan _lastReport;
        private string _activity = "startup";
        private bool _stalled;

        public MainThreadStallDetector(TimeSpan threshold, TimeSpan reportInterval)
        {
            _threshold = threshold;
            _reportInterval = reportInterval;
        }

        public StallEvent? Heartbeat(TimeSpan now, string activity, int inboxCount)
        {
            lock (_sync)
            {
                TimeSpan gap = now - _lastHeartbeat;
                StallEvent? recovered = null;
                if (_stalled)
                {
                    recovered = new StallEvent(
                        StallEventKind.Recovered,
                        now - _stallStart,
                        _activity,
                        inboxCount);
                }
                else if (gap >= _threshold)
                {
                    recovered = new StallEvent(
                        StallEventKind.Recovered,
                        gap,
                        _activity,
                        inboxCount);
                }

                _lastHeartbeat = now;
                _activity = activity;
                _stalled = false;
                return recovered;
            }
        }

        public void SetActivity(string activity)
        {
            lock (_sync)
                _activity = activity;
        }

        public StallEvent? Observe(TimeSpan now, int inboxCount)
        {
            lock (_sync)
            {
                TimeSpan gap = now - _lastHeartbeat;
                if (gap < _threshold)
                    return null;

                if (!_stalled)
                {
                    _stalled = true;
                    _stallStart = _lastHeartbeat;
                    _lastReport = now;
                    return new StallEvent(
                        StallEventKind.Started,
                        gap,
                        _activity,
                        inboxCount);
                }

                if (now - _lastReport < _reportInterval)
                    return null;

                _lastReport = now;
                return new StallEvent(
                    StallEventKind.Ongoing,
                    now - _stallStart,
                    _activity,
                    inboxCount);
            }
        }
    }
}
