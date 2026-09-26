namespace Goose2Client
{
    /// <summary>
    /// Tracks the hotkey press that is currently down and reports the moment it has been held long
    /// enough to count as a cast. A press fires at most once and then stays in repeating mode until
    /// the key comes up, so no hold state can survive into a later press.
    /// </summary>
    public sealed class HoldCastGate
    {
        public const double DefaultDelaySeconds = 0.3;

        private readonly double _delaySeconds;
        private string _action;
        private double _heldSeconds;
        private bool _repeating;
        private bool _spent;

        public HoldCastGate(double delaySeconds = DefaultDelaySeconds) => _delaySeconds = delaySeconds;

        /// <summary>Hotkey action of the tracked press, or null when no hotkey is down.</summary>
        public string Action => _action;

        /// <summary>This press already hold-cast and keeps casting until the key is released.</summary>
        public bool IsRepeating => _action != null && _repeating;

        /// <summary>Advances the tracked press; true exactly once per press, when the delay elapses.</summary>
        public bool Update(string heldAction, double delta)
        {
            if (heldAction != _action)
            {
                _action = heldAction;
                _heldSeconds = 0;
                _repeating = false;
                _spent = false;
            }

            if (heldAction == null) return false;

            _heldSeconds += delta;
            if (_repeating || _spent || _heldSeconds < _delaySeconds) return false;

            _repeating = true;
            return true;
        }

        /// <summary>Ends the tracked press (manual confirm or cancel) — it stays inert until released.</summary>
        public void Spend()
        {
            if (_action == null) return;
            _spent = true;
            _repeating = false;
        }
    }
}
