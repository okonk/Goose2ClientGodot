using Godot;

namespace Goose2Client.Overlays
{
    public partial class BattleTextLine : WorldOverlay
    {
        private Label _label;
        private Vector2 _baseOffset;
        private float _scale = 1f;
        private float _worldScale = 1f;

        public void Initialize(Color color, string text, Vector2 baseOffset, float textScale, float worldScale)
        {
            _baseOffset = baseOffset;

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

            Lifetime = new OverlayLifetime(1.0, risePixelsPerSecond: 32);
        }

        public void ApplyScale(float textScale, float worldScale)
        {
            _scale = textScale;
            _worldScale = worldScale;
            _label.AddThemeFontSizeOverride("font_size", Mathf.Max(1, Mathf.RoundToInt(12f * textScale)));
            _label.AddThemeConstantOverride("outline_size", Mathf.RoundToInt(4f * textScale));
            _label.Size = new Vector2(100f, 16f) * textScale;
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
