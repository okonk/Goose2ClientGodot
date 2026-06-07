using Godot;

namespace Goose2Client.Overlays
{
    /// <summary>A single floating battle text line. Rises upward over its lifetime then self-frees.</summary>
    public partial class BattleTextLine : WorldOverlay
    {
        private Label _label;
        private Vector2 _baseOffset;

        /// <summary>Initialize the line: create the label, set color/text, and start the 1s rise.</summary>
        public void Initialize(Color color, string text, Vector2 baseOffset)
        {
            _baseOffset = baseOffset;

            _label = new Label
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                ZIndex = 20,
                Size = new Vector2(100, 16),
            };
            _label.AddThemeFontSizeOverride("font_size", 12);
            _label.AddThemeConstantOverride("outline_size", 4);
            _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            _label.AddThemeColorOverride("font_color", color);
            // Anchor the 100px-wide label by its CENTER on the node origin (= character center),
            // so HorizontalAlignment.Center actually centers the text on the character.
            _label.Position = new Vector2(-_label.Size.X / 2f, 0);
            AddChild(_label);

            // 1.0s lifetime, rising at 32 px/s (1 world-unit/s = 32 px/s)
            Lifetime = new OverlayLifetime(1.0, risePixelsPerSecond: 32);
        }

        protected override void Tick(double delta)
        {
            // Rise upward (negative Y in Godot) over lifetime
            Position = _baseOffset - new Vector2(0, (float)Lifetime.RiseOffsetPixels);
        }
    }
}
