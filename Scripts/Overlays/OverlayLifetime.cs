namespace Goose2Client.Overlays
{
    /// <summary>Pure lifetime/rise accumulator for world overlays (battle text, bubbles, spell fx).</summary>
    public sealed class OverlayLifetime
    {
        private readonly double _duration, _riseRate;
        public double Elapsed { get; private set; }
        public OverlayLifetime(double durationSeconds, double risePixelsPerSecond = 0)
        { _duration = durationSeconds; _riseRate = risePixelsPerSecond; }
        public void Advance(double delta) => Elapsed += delta;
        public bool Expired => Elapsed >= _duration;
        public double RiseOffsetPixels => Elapsed * _riseRate;
    }
}
