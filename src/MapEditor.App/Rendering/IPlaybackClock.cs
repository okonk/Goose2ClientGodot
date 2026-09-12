using System;

namespace MapEditor.App.Rendering;

internal interface IPlaybackClock : IDisposable
{
    event EventHandler? Tick;

    TimeSpan Interval { get; set; }

    bool IsRunning { get; }

    void Start();

    void Stop();
}
