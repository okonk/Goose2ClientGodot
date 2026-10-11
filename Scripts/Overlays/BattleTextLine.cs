using Godot;

namespace Goose2Client.Overlays
{
    public partial class BattleTextLine : WorldOverlay
    {
        private const float LabelWidth = 100f;
        public const float LabelHeight = 16f;
        private const double RisePixelsPerSecond = 32;
        private const double RiseCapPixels = 48;

        private Label _label = null!;
        private Vector2 _baseOffset;
        private float _scale = 1f;
        private float _worldScale = 1f;

        public void Initialize(Color color, string text, Vector2 baseOffset, float textScale, float worldScale, bool accumulating = false)
        {
            _baseOffset = baseOffset;
            Lifetime = accumulating
                ? new OverlayLifetime(double.PositiveInfinity, RisePixelsPerSecond)
                : new OverlayLifetime(1.0, RisePixelsPerSecond);
            Position = _baseOffset * worldScale;

            _label = new Label
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                ZIndex = 20,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            _label.AddThemeColorOverride("font_color", color);
            ApplyScale(textScale, worldScale);
            AddChild(_label);
        }

        public float CurrentScreenOffsetY() =>
            (_baseOffset.Y - (float)Lifetime.RiseOffsetPixels) * _worldScale;

        public void PushUpScreenPixels(float pixels)
        {
            if (_worldScale <= 0f) return;
            _baseOffset.Y -= pixels / _worldScale;
        }

        public void SetAccumulatedText(string text) => _label.Text = text;

        public void BeginRise()
        {
            double risen = Mathf.Min(Lifetime.RiseOffsetPixels, RiseCapPixels);
            _baseOffset.Y -= (float)risen;
            Lifetime = new OverlayLifetime(Mathf.Min(1.0, (RiseCapPixels - risen) / RisePixelsPerSecond), RisePixelsPerSecond);
        }

        public void ApplyScale(float textScale, float worldScale)
        {
            _scale = textScale;
            _worldScale = worldScale;
            _label.AddThemeFontSizeOverride("font_size", Mathf.Max(1, Mathf.RoundToInt(12f * textScale)));
            _label.AddThemeConstantOverride("outline_size", Mathf.RoundToInt(4f * textScale));
            _label.Size = new Vector2(LabelWidth, LabelHeight) * textScale;
            _label.Position = new Vector2(-_label.Size.X / 2f, 0);
        }

        protected override void Tick(double delta)
        {
            // _baseOffset is world units — converted to screen px with the world scale
            // (the rise term rides the same conversion, as before the UI-scale split).
            Position = (_baseOffset - new Vector2(0, (float)Lifetime.RiseOffsetPixels)) * _worldScale;
        }
    }
}
