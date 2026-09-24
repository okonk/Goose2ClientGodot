namespace Goose2Client
{
    public sealed class HoldConfirmTimer
    {
        public const double DefaultDelaySeconds = 0.25;

        private readonly double _delaySeconds;
        private string _action;
        private double _heldSeconds;

        public HoldConfirmTimer(double delaySeconds = DefaultDelaySeconds) => _delaySeconds = delaySeconds;

        public bool Tick(string heldAction, double delta)
        {
            if (heldAction == null || heldAction != _action)
            {
                _action = heldAction;
                _heldSeconds = 0;
            }

            if (heldAction == null) return false;

            _heldSeconds += delta;
            return _heldSeconds >= _delaySeconds;
        }
    }
}
