using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Goose2Client.Diagnostics
{
    public static class MainThreadStallLog
    {
        public static string Format(
            StallEvent stallEvent,
            DateTimeOffset timestamp,
            long managedBytes)
        {
            string kind = stallEvent.Kind.ToString().ToUpperInvariant();
            double managedMegabytes = managedBytes / (1024d * 1024d);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{timestamp:O} STALL {kind} duration={stallEvent.Duration.TotalSeconds:F1}s activity={stallEvent.Activity} inbox={stallEvent.InboxCount} managed_mb={managedMegabytes:F1}");
        }
    }

    public sealed class MainThreadStallMonitor : IDisposable
    {
        private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(1);
        private readonly MainThreadStallDetector _detector = new(Threshold, ReportInterval);
        private readonly ConcurrentQueue<StallEvent> _pending = new();
        private readonly Func<int> _inboxCount;
        private readonly long _startedAt = Stopwatch.GetTimestamp();
        private readonly Timer? _timer;
        private int _checking;
        private volatile bool _disposed;

        public string LogPath { get; }
        public bool IsEnabled { get; }
        public string? InitializationError { get; }

        public MainThreadStallMonitor(string logPath, Func<int> inboxCount)
        {
            LogPath = logPath;
            _inboxCount = inboxCount;
            _detector.Heartbeat(Now(), "startup", 0);

            try
            {
                string? directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.AppendAllText(
                    logPath,
                    $"{DateTimeOffset.UtcNow:O} SESSION START pid={Environment.ProcessId}{Environment.NewLine}");
                IsEnabled = true;
            }
            catch (Exception e)
            {
                InitializationError = e.Message;
                return;
            }

            _timer = new Timer(Check, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public void Heartbeat(string activity)
        {
            StallEvent? recovered = _detector.Heartbeat(Now(), activity, _inboxCount());
            if (recovered.HasValue)
                _pending.Enqueue(recovered.Value);
        }

        public void SetActivity(string activity) => _detector.SetActivity(activity);

        private TimeSpan Now() => Stopwatch.GetElapsedTime(_startedAt);

        private void Check(object? state)
        {
            if (_disposed || Interlocked.Exchange(ref _checking, 1) != 0)
                return;

            try
            {
                StallEvent? observed = _detector.Observe(Now(), _inboxCount());
                if (observed.HasValue)
                    _pending.Enqueue(observed.Value);

                while (_pending.TryDequeue(out StallEvent stallEvent))
                {
                    string line = MainThreadStallLog.Format(
                        stallEvent,
                        DateTimeOffset.UtcNow,
                        GC.GetTotalMemory(false));
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
            catch
            {
            }
            finally
            {
                Volatile.Write(ref _checking, 0);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _timer?.Dispose();
        }
    }
}
