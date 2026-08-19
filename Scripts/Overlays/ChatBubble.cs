using Godot;

namespace Goose2Client.Overlays
{
    /// <summary>A speech chat bubble displayed above a speaking character.
    /// Wraps text, shows a padded background, and self-frees after 3 seconds.</summary>
    public partial class ChatBubble : WorldOverlay
    {
        private Panel _background;
        private Label _label;

        /// <summary>The final background pixel width (used by the caller for centering).
        /// The height is not reported: the node origin is the background's bottom-left, so
        /// the caller just anchors the bubble's bottom edge above the nameplate.</summary>
        public float BackgroundWidth { get; private set; }

        public override void _Ready()
        {
            // 3.0s lifetime, no rise — bubble sits still above the nameplate.
            Lifetime = new OverlayLifetime(ChatBubbleLayout.LifetimeSeconds);
            // Absolute z just above name labels (NamesZIndex) so the bubble draws on top of
            // player names, matching the reference client (bubble 1002 > names 1000).
            ZIndex = Constants.NamesZIndex + 2;
            ZAsRelative = false;
        }

        /// <summary>Set the bubble text, measure, wrap if needed, and size the background.</summary>
        public void SetText(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            const int fontSize = 12;

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

            // Create label (NOT added to the tree yet — see below). For wrapped text the
            // autowrap mode and wrap width must be set before it enters the tree: once in
            // the tree, a layout flush clamps its size to the min size cached from whichever
            // state the label was in when it entered — with autowrap still off that min width
            // is the full unwrapped text width, so the label is silently stretched past the
            // wrap width and the text renders on one line. (Verified with
            // tools/bubble_order_test.gd: pre-add setup stays stable, post-add gets clobbered.)
            _label = new Label
            {
                Text = message,
                VerticalAlignment = VerticalAlignment.Center,
                ZIndex = 21,
                // The wrapped label rect is wider than the (text-hugging) background, so the
                // transparent overhang must not steal mouse input.
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _label.AddThemeFontSizeOverride("font_size", fontSize);
            _label.AddThemeConstantOverride("outline_size", 4);
            _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            _label.AddThemeColorOverride("font_color", Colors.White);

            // Measurement (reference-client flow, verified with the tools/bubble_*.gd probes
            // on Godot 4.6.2):
            //  - One-liners: plain font metrics.
            //  - Wrapped text: enable WordSmart and the wrap width BEFORE the label enters
            //    the tree, then read the wrapped height from the label's OWN min size on the
            //    same frame. That read is only valid with WordSmart — with plain Word the min
            //    size is still the pre-layout placeholder one frame early (bubble_autowrap_min
            //    _test.gd). WordSmart also hard-breaks words wider than the wrap width (URLs,
            //    text typed without spaces) and its min-size math accounts for those breaks,
            //    which FontData.GetMultilineStringSize does not (it reports such a word as one
            //    over-wide line — bubble_nospace_test.gd). So the label's min size is the
            //    source of truth for the height, and the font only supplies the width.
            var font = _label.GetThemeFont("font") ?? _label.GetThemeDefaultFont();
            float maxTextWidth = ChatBubbleLayout.MaxWidth;
            float textWidth;
            float textHeight;

            if (font == null)
            {
                // Effectively unreachable: the theme always provides a default font.
                textWidth = maxTextWidth;
                textHeight = 14f;
            }
            else
            {
                Vector2 natural = font.GetStringSize(message, HorizontalAlignment.Left, -1, fontSize);
                if (natural.X <= maxTextWidth)
                {
                    // Fits on one line — shrink bubble to fit.
                    textWidth = natural.X;
                    textHeight = font.GetHeight(fontSize);
                }
                else
                {
                    // Wrapped: width hugs the longest line; height comes from the label itself.
                    _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    _label.Size = new Vector2(maxTextWidth, 0);
                    AddChild(_label);
                    Vector2 wrapped = font.GetMultilineStringSize(message, HorizontalAlignment.Left, maxTextWidth, fontSize);
                    textWidth = Mathf.Min(wrapped.X, maxTextWidth);
                    textHeight = _label.GetMinimumSize().Y;
                }
            }

            var textSize = new Vector2(textWidth, textHeight);

            // Compute background size with padding. The node origin is the background's
            // bottom-left corner (whole background sits above y=0), so the caller anchors
            // the bubble's bottom edge above the nameplate and any later height correction
            // grows the bubble upward, away from the name, without moving the node.
            var bgSize = ChatBubbleLayout.BackgroundSize(textSize);
            BackgroundWidth = bgSize.X;
            _background.Size = bgSize;
            _background.Position = new Vector2(0, -bgSize.Y);

            // Size the label to the measured text block, inset by the padding. For wrapped
            // text the label keeps the full max text width (not the longest line) so its
            // internal reflow matches the measurement above exactly; the extra width is a
            // transparent overhang beyond the background. (In the wrapped case the label was
            // already added during measurement — see above.)
            _label.Position = new Vector2(ChatBubbleLayout.Padding.X, ChatBubbleLayout.Padding.Y - bgSize.Y);
            _label.Size = _label.AutowrapMode != TextServer.AutowrapMode.Off
                ? new Vector2(ChatBubbleLayout.MaxWidth, textSize.Y)
                : textSize;
            if (_label.GetParent() == null) AddChild(_label);
        }
    }
}
