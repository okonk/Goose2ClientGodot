using System;
using MapEditor.App.Rendering;

namespace MapEditor.App.Tests.Fakes;

public sealed class ManualPlaybackClock : IPlaybackClock
{
    public event EventHandler? Tick;

    public TimeSpan Interval { get; set; }

    public bool IsRunning { get; private set; }

    public int DisposeCount { get; private set; }

    public void Start()
    {
        if (DisposeCount == 0)
        {
            IsRunning = true;
        }
    }

    public void Stop()
    {
        IsRunning = false;
    }

    public void Advance()
    {
        if (IsRunning && DisposeCount == 0)
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        DisposeCount++;
        IsRunning = false;
    }
}
