using Godot;

namespace Goose2Client.Overlays
{
    /// <summary>A single floating battle text line. Rises upward over its lifetime then self-frees.</summary>
    public partial class BattleTextLine : WorldOverlay
    {
        private Label _label;
        private Vector2 _baseOffset;
        private float _scale = 1f;

        /// <summary>Initialize the line: create the label, set color/text, and start the 1s rise.
        /// <paramref name="scale"/> is the bridge display scale — visual constants are in SCREEN px.</summary>
        public void Initialize(Color color, string text, Vector2 baseOffset, float scale)
        {
            // STAYS WORLD UNITS — spread grid is world-unit offsets, scaled at projection in Tick.
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
            ApplyScale(scale);
            AddChild(_label);

            // 1.0s lifetime, rising at 32 world-px/s
            Lifetime = new OverlayLifetime(1.0, risePixelsPerSecond: 32);
        }

        /// <summary>Apply the bridge display scale to the existing label (font/outline/size/position).
        /// _baseOffset is untouched — it is in world units.</summary>
        public void ApplyScale(float scale)
        {
            _scale = scale;
            _label.AddThemeFontSizeOverride("font_size", Mathf.Max(1, Mathf.RoundToInt(12f * scale)));
            _label.AddThemeConstantOverride("outline_size", Mathf.RoundToInt(4f * scale));
            _label.Size = new Vector2(100f, 16f) * scale;
            // Center the label HORIZONTALLY on the node origin (= character center); its top
            // edge sits AT the origin (y = 0), so HorizontalAlignment.Center centers the text
            // on the character (see the BattleText.ScreenBounds y-extent derivation).
            _label.Position = new Vector2(-_label.Size.X / 2f, 0);
        }

        protected override void Tick(double delta)
        {
            // Rise upward (negative Y in Godot) over lifetime. World units → screen px.
            Position = (_baseOffset - new Vector2(0, (float)Lifetime.RiseOffsetPixels)) * _scale;
        }
    }
}
