using Godot;

namespace Goose2Client.UI
{
    public partial class BuffSweepBar : Control
    {
        private double _remaining;
        private double _dangerSeconds;
        private float _progress;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public void Update(double remainingSeconds, double totalSeconds, double dangerSeconds = 0)
        {
            _remaining = remainingSeconds;
            _dangerSeconds = dangerSeconds;
            _progress = CooldownOverlay.ComputeProgress(remainingSeconds, totalSeconds, growthMode: true);
            Visible = totalSeconds > 0;
            QueueRedraw();
        }

        internal static Color SelectSweepColor(double remainingSeconds, double dangerSeconds)
            => dangerSeconds > 0 && remainingSeconds <= dangerSeconds
                ? new Color(0.8f, 0.1f, 0.1f, 0.7f)
                : new Color(0, 0, 0, 0.7f);

        public override void _Draw()
        {
            if (_progress <= 0f)
                return;

            var size = GetSize();
            var barHeight = size.Y * _progress;
            DrawRect(new Rect2(0, size.Y - barHeight, size.X, barHeight), SelectSweepColor(_remaining, _dangerSeconds));
        }
    }
}
