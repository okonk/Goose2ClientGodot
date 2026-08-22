using Godot;

namespace Goose2Client.Overlays
{
    public partial class ChatBubble : WorldOverlay, IBridgedText
    {
        private Panel _background;
        private Label _label;
        private Vector2 _bgScreen;
        private string _message;
        private float _worldScale = 1f;

        public Character.Character AnchorOwner { get; set; }

        public Vector2 LocalOffsetWorld { get; set; }

        /// Text scale (UI factor). Named DisplayScale: `Scale` would shadow CanvasItem.Scale (Vector2) (CS0108).
        public float DisplayScale { get; private set; } = 1f;

        /// Cull rect = the background panel only; in the wrapped case the label overhangs it (transparent, MouseFilter.Ignore).
        public Rect2 ScreenBounds => new Rect2(0f, -_bgScreen.Y, _bgScreen.X, _bgScreen.Y);

        /// Background width in WORLD units; the node origin is the background's bottom-left (−width/2 centers the bubble).
        public float BackgroundWidth { get; private set; }

        public override void _Ready()
        {
            Lifetime = new OverlayLifetime(ChatBubbleLayout.LifetimeSeconds);
            // Just above name labels (NamesZIndex) so the bubble draws on top of player names (reference: 1002 > 1000).
            ZIndex = Constants.NamesZIndex + 2;
            ZAsRelative = false;
        }

        public void UpdateAnchor()
        {
            if (AnchorOwner == null || !GodotObject.IsInstanceValid(AnchorOwner) || _message == null) return;
            // 48 fallback mirrors the name label's `Height <= 0 ? 48 : Height` — a character missing
            // body-height metadata must anchor above the same fallback the nameplate uses, or the bubble detaches.
            int bodyHeight = AnchorOwner.Height <= 0 ? 48 : AnchorOwner.Height;
            LocalOffsetWorld = new Vector2(
                -BackgroundWidth / 2f,
                -(bodyHeight + Character.Character.NameTopOffset)
                    - ChatBubbleLayout.VerticalGap);   // Character.Character: from Overlays, bare `Character` is the Goose2Client.Character namespace
        }

        public void ApplyScale(float textScale, float worldScale)
        {
            if (_message == null || (DisplayScale == textScale && _worldScale == worldScale)) return;
            SetText(_message, textScale, worldScale);
        }

        public void SetText(string message, float textScale, float worldScale)
        {
            if (string.IsNullOrEmpty(message)) return;
            _message = message;
            DisplayScale = textScale;
            _worldScale = worldScale;

            int fontSize = Mathf.Max(1, Mathf.RoundToInt(12f * textScale));
            float maxTextWidth = ChatBubbleLayout.MaxWidth * textScale;
            Vector2 padding = ChatBubbleLayout.Padding * textScale;

            if (_background == null)
            {
                _background = new Panel
                {
                    ZIndex = 20,
                    // A Stop Panel in the root viewport would swallow world clicks — bubble clicks must fall through.
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                AddChild(_background);
            }

            var styleBox = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.15f, 0.15f, 0.85f),
                CornerRadiusTopLeft = Mathf.RoundToInt(6f * textScale),
                CornerRadiusTopRight = Mathf.RoundToInt(6f * textScale),
                CornerRadiusBottomLeft = Mathf.RoundToInt(6f * textScale),
                CornerRadiusBottomRight = Mathf.RoundToInt(6f * textScale),
            };
            _background.AddThemeStyleboxOverride("panel", styleBox);

            // Autowrap mode + wrap width must be set before the label enters the tree: once in the tree, a
            // layout flush clamps its size to the pre-wrap cached min size, and OFF→ON does not reflow (probed).
            if (_label == null)
            {
                _label = new Label
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    ZIndex = 21,
                    // The wrapped label rect overhangs the background — the transparent overhang must not steal mouse input.
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
            }
            _label.Text = message;
            _label.AddThemeFontSizeOverride("font_size", fontSize);
            _label.AddThemeConstantOverride("outline_size", Mathf.RoundToInt(4f * textScale));
            _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            _label.AddThemeColorOverride("font_color", Colors.White);

            // Wrapped height is read from the label's min size, only valid with WordSmart set before add
            // (probed: with plain Word it is a stale one-frame placeholder; WordSmart's hard breaks are
            // what FontData.GetMultilineStringSize misses).
            if (_label.GetParent() != null)
                RemoveChild(_label);   // in-tree autowrap OFF→ON does not reflow (probed); re-added in final state below
            var font = _label.GetThemeFont("font") ?? _label.GetThemeDefaultFont();
            float textWidth;
            float textHeight;

            if (font == null)
            {
                // Effectively unreachable: the theme always provides a default font.
                textWidth = maxTextWidth;
                textHeight = 14f * textScale;
            }
            else
            {
                Vector2 natural = font.GetStringSize(message, HorizontalAlignment.Left, -1, fontSize);
                if (natural.X <= maxTextWidth)
                {
                    _label.AutowrapMode = TextServer.AutowrapMode.Off;   // reset for reuse: a previously-wrapped label
                                                                         // must not reflow the new shorter text
                    textWidth = natural.X;
                    textHeight = font.GetHeight(fontSize);
                }
                else
                {
                    _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    _label.Size = new Vector2(maxTextWidth, 0);
                    if (_label.GetParent() == null) AddChild(_label);
                    Vector2 wrapped = font.GetMultilineStringSize(message, HorizontalAlignment.Left, maxTextWidth, fontSize);
                    textWidth = Mathf.Min(wrapped.X, maxTextWidth);
                    textHeight = _label.GetMinimumSize().Y;
                }
            }

            var textSize = new Vector2(textWidth, textHeight);

            var bgSize = textSize + padding * 2;
            // WORLD units (self-anchoring is world-unit) — the world scale, not the text scale:
            // the bubble's screen width must map back through the world's px-per-world-unit.
            BackgroundWidth = bgSize.X / worldScale;
            _bgScreen = bgSize;
            _background.Size = bgSize;
            _background.Position = new Vector2(0, -bgSize.Y);

            // Wrapped label keeps the full max text width (not the longest line) so its internal reflow
            // matches the measurement above exactly; the extra width is a transparent overhang.
            _label.Position = new Vector2(padding.X, padding.Y - bgSize.Y);
            _label.Size = _label.AutowrapMode != TextServer.AutowrapMode.Off
                ? new Vector2(maxTextWidth, textSize.Y)
                : textSize;
            if (_label.GetParent() == null) AddChild(_label);

            UpdateAnchor();   // re-measure may have changed BackgroundWidth (wrap count is not
                              // strictly scale-invariant) — the anchor must follow
        }
    }
}
