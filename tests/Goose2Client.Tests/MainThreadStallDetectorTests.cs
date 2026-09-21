using System;
using Goose2Client.Diagnostics;
using Xunit;

namespace Goose2Client.Tests;

public class MainThreadStallDetectorTests
{
    [Fact]
    public void Observe_BeforeThreshold_ReturnsNothing()
    {
        var detector = CreateDetector();
        detector.Heartbeat(TimeSpan.Zero, "frame-complete", 0);

        StallEvent? result = detector.Observe(TimeSpan.FromSeconds(4.9), 2);

        Assert.Null(result);
    }

    [Fact]
    public void Observe_ReportsStartThenOneReminderPerInterval()
    {
        var detector = CreateDetector();
        detector.Heartbeat(TimeSpan.Zero, "packet SCM", 0);

        StallEvent? started = detector.Observe(TimeSpan.FromSeconds(5), 12);
        StallEvent? suppressed = detector.Observe(TimeSpan.FromSeconds(64), 20);
        StallEvent? ongoing = detector.Observe(TimeSpan.FromSeconds(65), 24);

        Assert.Equal(StallEventKind.Started, started?.Kind);
        Assert.Equal(TimeSpan.FromSeconds(5), started?.Duration);
        Assert.Equal("packet SCM", started?.Activity);
        Assert.Equal(12, started?.InboxCount);
        Assert.Null(suppressed);
        Assert.Equal(StallEventKind.Ongoing, ongoing?.Kind);
        Assert.Equal(TimeSpan.FromSeconds(65), ongoing?.Duration);
        Assert.Equal(24, ongoing?.InboxCount);
    }

    [Fact]
    public void Heartbeat_AfterReportedStall_ReturnsRecovery()
    {
        var detector = CreateDetector();
        detector.Heartbeat(TimeSpan.Zero, "network-send socket", 0);
        detector.Observe(TimeSpan.FromSeconds(5), 1);

        StallEvent? recovered = detector.Heartbeat(
            TimeSpan.FromMinutes(8),
            "frame-start",
            300);

        Assert.Equal(StallEventKind.Recovered, recovered?.Kind);
        Assert.Equal(TimeSpan.FromMinutes(8), recovered?.Duration);
        Assert.Equal("network-send socket", recovered?.Activity);
        Assert.Equal(300, recovered?.InboxCount);
        Assert.Null(detector.Observe(TimeSpan.FromMinutes(8), 300));
    }

    [Fact]
    public void Heartbeat_AfterUnobservedGap_ReturnsRecovery()
    {
        var detector = CreateDetector();
        detector.Heartbeat(TimeSpan.Zero, "frame-complete", 0);

        StallEvent? recovered = detector.Heartbeat(
            TimeSpan.FromSeconds(30),
            "frame-start",
            40);

        Assert.Equal(StallEventKind.Recovered, recovered?.Kind);
        Assert.Equal(TimeSpan.FromSeconds(30), recovered?.Duration);
        Assert.Equal("frame-complete", recovered?.Activity);
        Assert.Equal(40, recovered?.InboxCount);
    }

    private static MainThreadStallDetector CreateDetector()
        => new(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
}

public class MainThreadStallLogTests
{
    [Fact]
    public void Format_IncludesDiagnosticFieldsWithoutPacketPayloads()
    {
        var stallEvent = new StallEvent(
            StallEventKind.Started,
            TimeSpan.FromSeconds(5.26),
            "packet SCM",
            17);

        string line = MainThreadStallLog.Format(
            stallEvent,
            new DateTimeOffset(2026, 9, 21, 8, 30, 0, TimeSpan.Zero),
            12 * 1024 * 1024);

        Assert.Contains("2026-09-21T08:30:00.0000000+00:00", line);
        Assert.Contains("STALL STARTED", line);
        Assert.Contains("duration=5.3s", line);
        Assert.Contains("activity=packet SCM", line);
        Assert.Contains("inbox=17", line);
        Assert.Contains("managed_mb=12.0", line);
    }
}
