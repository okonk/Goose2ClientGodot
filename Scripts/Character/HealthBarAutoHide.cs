namespace Goose2Client.Character;

/// <summary>
/// Auto-hide logic for overhead health/mana bars (Unity CharacterHealthBars parity).
/// Bars show on any vital change. They schedule a hide only when BOTH vitals are full
/// (hpPercent >= 1 && mpPercent >= 1). Any partial value cancels the pending hide.
/// </summary>
public sealed class HealthBarAutoHide
{
    public const double HideDelaySeconds = 2.0;
    private double _hideAt = double.PositiveInfinity;
    private bool _hasLast;
    private float _lastHp;
    private float _lastMp;

    /// <summary>Whether the overhead bars should be visible.</summary>
    public bool Visible { get; private set; } = true;

    /// <summary>
    /// Called when vitals change. Always shows the bars. Schedules a hide at
    /// <paramref name="nowSeconds"/> + 2s only if both vitals are full (>= 1).
    /// Any partial value cancels a pending hide.
    /// </summary>
    public void OnVitalsChanged(float hpPercent, float mpPercent, double nowSeconds)
    {
        if (_hasLast && hpPercent == _lastHp && mpPercent == _lastMp) return;
        _hasLast = true;
        _lastHp = hpPercent;
        _lastMp = mpPercent;

        Visible = true;

        if (hpPercent >= 1f && mpPercent >= 1f)
            _hideAt = nowSeconds + HideDelaySeconds;
        else
            _hideAt = double.PositiveInfinity;
    }

    /// <summary>
    /// Tick the auto-hide timer. If <paramref name="nowSeconds"/> has reached the hide deadline,
    /// hides the bars. Returns the current visibility state.
    /// </summary>
    public bool Tick(double nowSeconds)
    {
        if (nowSeconds >= _hideAt)
        {
            Visible = false;
            _hideAt = double.PositiveInfinity;
        }
        return Visible;
    }
}
