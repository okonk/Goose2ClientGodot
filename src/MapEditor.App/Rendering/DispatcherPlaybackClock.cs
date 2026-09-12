using System;
using Avalonia.Threading;

namespace MapEditor.App.Rendering;

internal sealed class DispatcherPlaybackClock : IPlaybackClock
{
    private readonly DispatcherTimer _timer;

    public DispatcherPlaybackClock()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Normal);
        _timer.Tick += OnTimerTick;
    }

    public event EventHandler? Tick;

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public bool IsRunning => _timer.IsEnabled;

    public void Start()
        => _timer.Start();

    public void Stop()
        => _timer.Stop();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private void OnTimerTick(object? sender, EventArgs e)
        => Tick?.Invoke(this, EventArgs.Empty);
}
