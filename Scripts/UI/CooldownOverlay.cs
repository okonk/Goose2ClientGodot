using System.Collections.Generic;
using Godot;

namespace Goose2Client.UI
{
    // Mirrors the original Unity client's cooldown display: a black pie that
    // covers the icon at cast time and unwinds clockwise from the top, with a
    // centered countdown (whole seconds while >= 1s, one decimal below that).
    public partial class CooldownOverlay : Control
    {
        private Label _text;
        private float _progress;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public void Update(double remainingSeconds, double totalSeconds)
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
                _text.SetAnchorsPreset(LayoutPreset.FullRect);
                AddChild(_text);
            }

            if (remainingSeconds <= 0 || totalSeconds <= 0)
            {
                if (!Visible)
                    return;
                _progress = 0f;
                _text.Visible = false;
                Visible = false;
                return;
            }

            _progress = Mathf.Clamp((float)(remainingSeconds / totalSeconds), 0f, 1f);
            Visible = true;
            _text.Visible = true;
            _text.Text = remainingSeconds >= 1
                ? Mathf.CeilToInt(remainingSeconds).ToString()
                : remainingSeconds.ToString("F1");
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
            DrawColoredPolygon(points.ToArray(), new Color(0, 0, 0, 0.7f));
        }
    }
}
