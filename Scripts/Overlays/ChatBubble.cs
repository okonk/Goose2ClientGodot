using Godot;

namespace Goose2Client.Overlays
{
    /// <summary>A speech chat bubble displayed above a speaking character.
    /// Wraps text, shows a padded background, and self-frees after 3 seconds.</summary>
    public partial class ChatBubble : WorldOverlay
    {
        private Panel _background;
        private Label _label;

        /// <summary>The final background pixel height (used by the caller for positioning).</summary>
        public float BackgroundHeight { get; private set; }

        public override void _Ready()
        {
            // 3.0s lifetime, no rise — bubble sits still above the character head
            Lifetime = new OverlayLifetime(ChatBubbleLayout.LifetimeSeconds);
        }

        /// <summary>Set the bubble text, measure, wrap if needed, and size the background.</summary>
        public void SetText(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            const int fontSize = 14;

            // Create background panel
            _background = new Panel
            {
                ZIndex = 20,
            };
            AddChild(_background);

            // Style the panel background
            var styleBox = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.15f, 0.15f, 0.85f),
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6,
            };
            _background.AddThemeStyleboxOverride("panel", styleBox);

            // Create label
            _label = new Label
            {
                Text = message,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ZIndex = 21,
            };
            _label.AddThemeFontSizeOverride("font_size", fontSize);
            _label.AddThemeConstantOverride("outline_size", 4);
            _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            _label.AddThemeColorOverride("font_color", Colors.White);
            _background.AddChild(_label);

            // Measure text
            var defaultFont = _label.GetThemeDefaultFont();
            Vector2 textSize;

            if (defaultFont != null)
            {
                textSize = defaultFont.GetStringSize(
                    message,
                    HorizontalAlignment.Left,
                    -1, // no width limit initially
                    fontSize
                );
            }
            else
            {
                // Fallback: set text and read minimum size
                _label.Size = new Vector2(1, 1);
                textSize = _label.GetMinimumSize();
            }

            // If text exceeds max width, wrap it
            float clampedWidth = ChatBubbleLayout.ClampWidth(textSize.X);
            if (clampedWidth < textSize.X)
            {
                _label.AutowrapMode = TextServer.AutowrapMode.Word;
                _label.CustomMinimumSize = new Vector2(clampedWidth, 0);
                _label.Size = new Vector2(clampedWidth, 1);

                // Re-measure after wrapping
                if (defaultFont != null)
                {
                    textSize = defaultFont.GetStringSize(
                        message,
                        HorizontalAlignment.Left,
                        (int)clampedWidth,
                        fontSize
                    );
                }
                else
                {
                    textSize = _label.GetMinimumSize();
                }
            }

            // Compute background size with padding
            var bgSize = ChatBubbleLayout.BackgroundSize(textSize);
            BackgroundHeight = bgSize.Y;

            // Set background and label sizes
            _background.Size = bgSize;
            _label.Size = bgSize;
        }
    }
}
