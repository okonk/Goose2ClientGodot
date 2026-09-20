using System.Collections.Generic;
using Godot;

namespace Goose2Client.UI
{
    // Mirrors the original Unity client's cooldown display: a black pie that
    // covers the icon at cast time and unwinds clockwise from the top, with a
    // centered countdown (h/m at >= 1 hour, mm:ss at >= 1 minute, whole
    // seconds while >= 1s, one decimal below that).
    public partial class CooldownOverlay : Control
    {
        private Label _text;
        private float _progress;
        private double _remaining;
        private double _dangerSeconds;

        internal static string FormatCountdown(double remainingSeconds)
        {
            int totalSeconds = Mathf.CeilToInt(remainingSeconds);
            if (totalSeconds >= 3600)
            {
                int hours = totalSeconds / 3600;
                int minutes = (totalSeconds % 3600) / 60;
                return minutes > 0 ? $"{hours}h{minutes}m" : $"{hours}h";
            }

            if (totalSeconds >= 60)
                return $"{totalSeconds / 60}:{totalSeconds % 60:D2}";

            if (remainingSeconds >= 1)
                return totalSeconds.ToString();

            string subSecond = remainingSeconds.ToString("F1");
            return subSecond == "1.0" ? "1" : subSecond;
        }

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }

        internal static float ComputeProgress(double remainingSeconds, double totalSeconds, bool growthMode)
        {
            if (totalSeconds <= 0)
                return 0f;
            float ratio = (float)(remainingSeconds / totalSeconds);
            ratio = Mathf.Clamp(ratio, 0f, 1f);
            return growthMode ? 1f - ratio : ratio;
        }

        public void Update(double remainingSeconds, double totalSeconds, bool growthMode = false, double dangerSeconds = 0)
        {
            if (_text == null)
            {
                _text = new Label
                {
                    MouseFilter = MouseFilterEnum.Ignore,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _text.AddThemeColorOverride("font_color", Colors.White);
                _text.AddThemeColorOverride("font_outline_color", Colors.Black);
                _text.AddThemeConstantOverride("outline_size", 8);
                _text.SetAnchorsPreset(LayoutPreset.FullRect);
                AddChild(_text);
            }

            _remaining = remainingSeconds;
            _dangerSeconds = dangerSeconds;

            if (totalSeconds <= 0 || remainingSeconds <= 0)
            {
                if (growthMode && remainingSeconds <= 0)
                {
                    // Client clock can reach 0 before the server's remove packet lands; keep the pie full until the slot is cleared.
                    _progress = 1f;
                    Visible = true;
                    _text.Visible = true;
                    _text.Text = "0";
                    QueueRedraw();
                    return;
                }

                if (!Visible)
                    return;
                _progress = 0f;
                _text.Visible = false;
                Visible = false;
                return;
            }

            _progress = ComputeProgress(remainingSeconds, totalSeconds, growthMode);
            Visible = true;
            _text.Visible = true;
            _text.Text = FormatCountdown(remainingSeconds);
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_progress <= 0f)
                return;

            var center = GetSize() / 2f;
            var radius = Mathf.Min(center.X, center.Y);
            var points = new List<Vector2>(34) { center };
            const int segments = 32;
            for (int i = 0; i <= segments; i++)
            {
                var a = -Mathf.Pi / 2f + _progress * Mathf.Tau * (i / (float)segments);
                points.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
            var color = _dangerSeconds > 0 && _remaining <= _dangerSeconds
                ? new Color(0.8f, 0.1f, 0.1f, 0.7f)
                : new Color(0, 0, 0, 0.7f);
            DrawColoredPolygon(points.ToArray(), color);
        }
    }
}
