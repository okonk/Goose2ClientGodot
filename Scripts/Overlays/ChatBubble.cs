using Godot;

namespace Goose2Client.Overlays
{
    /// <summary>A speech chat bubble displayed above a speaking character.
    /// Wraps text, shows a padded background, and self-frees after 3 seconds.
    /// Rendered on the native-resolution <see cref="WorldTextBridge"/> (screen-px visuals,
    /// world-unit anchor) — <see cref="IBridgedText"/>.</summary>
    public partial class ChatBubble : WorldOverlay, IBridgedText
    {
        private Panel _background;
        private Label _label;
        private Vector2 _bgScreen;   // screen-px background size, set in SetText
        private string _message;

        /// <summary>The speaking character (set by the bridge at Register; used by self-anchoring).</summary>
        public Character.Character AnchorOwner { get; set; }

        /// <summary>Anchor offset from the owner's feet origin in WORLD units; re-derived in <see cref="UpdateAnchor"/>.</summary>
        public Vector2 LocalOffsetWorld { get; set; }

        /// <summary>The scale this bubble was measured at. Named DisplayScale (not Scale —
        /// Scale would shadow CanvasItem.Scale (Vector2) with a CS0108 warning).</summary>
        public float DisplayScale { get; private set; } = 1f;

        /// <summary>Local screen-space cull rect. Covers the BACKGROUND PANEL ONLY: in the wrapped
        /// case the label is MaxWidth·S wide while the panel hugs the longest line, so the label's
        /// transparent overhang can linger a frame or two past the display edge. Accepted per-rect
        /// imperfection — the overhang carries no visible glyphs (WordSmart hard-breaks keep
        /// fragments ≤ wrap width) and the label is MouseFilter.Ignore.</summary>
        public Rect2 ScreenBounds => new Rect2(0f, -_bgScreen.Y, _bgScreen.X, _bgScreen.Y);

        /// <summary>The final background width in WORLD units (used by self-anchoring: the node
        /// origin is the background's bottom-left, so −width/2 centers the bubble on the character).
        /// The height is not reported: the node origin is the background's bottom-left, so the
        /// anchor just places the bubble's bottom edge above the nameplate.</summary>
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

        /// <summary>Re-derive LocalOffsetWorld from the measured background width and the owner's body
        /// height. Called after every (re)measure (SetText) and by Character.RepositionOverlays (appearance/
        /// mount changes move the nameplate).</summary>
        public void UpdateAnchor()
        {
            if (AnchorOwner == null || !GodotObject.IsInstanceValid(AnchorOwner) || _message == null) return;
            // 48 fallback mirrors the name label's `Height <= 0 ? 48 : Height` (BridgedNameLabel) —
            // a character missing body-height metadata must anchor the bubble above the SAME fallback
            // body height the nameplate uses, or the bubble detaches from the nameplate.
            int bodyHeight = AnchorOwner.Height <= 0 ? 48 : AnchorOwner.Height;
            LocalOffsetWorld = new Vector2(
                -BackgroundWidth / 2f,
                -(bodyHeight + Character.Character.NameTopOffset)
                    - ChatBubbleLayout.VerticalGap);   // Character.Character: from Overlays, bare `Character` is the Goose2Client.Character namespace
        }

        /// <summary>Re-derive all visual constants at base × scale. No-op until measured or at the
        /// current scale (so Register's pre-SetText ApplyScale doesn't double-measure).</summary>
        public void ApplyScale(float scale)
        {
            if (_message == null || DisplayScale == scale) return;
            SetText(_message, scale);
        }

        /// <summary>Set the bubble text, measure, wrap if needed, and size the background. All visual
        /// constants are base × <paramref name="scale"/> (screen px); the anchor is re-derived at the end.
        /// Safe to re-call on a live bubble for SAME-message scale-change re-measures: existing
        /// panel/label are reused (remove-first reflow makes re-measure valid). A DIFFERENT message
        /// on a live one-line bubble must use a fresh bubble: an Off→WordSmart re-measure reads the
        /// label's min size stale (one-line placeholder) same-frame (probed) — Character always
        /// creates a new bubble per message, so this is unreachable today.</summary>
        public void SetText(string message, float scale)
        {
            if (string.IsNullOrEmpty(message)) return;
            _message = message;
            DisplayScale = scale;

            int fontSize = Mathf.Max(1, Mathf.RoundToInt(12f * scale));
            float maxTextWidth = ChatBubbleLayout.MaxWidth * scale;
            Vector2 padding = ChatBubbleLayout.Padding * scale;

            // Create background panel (first call) or reuse it (scale change / new message —
            // a naive re-create would double-add a second child).
            if (_background == null)
            {
                _background = new Panel
                {
                    ZIndex = 20,
                    // T5: a Stop Panel in the root viewport would swallow world clicks;
                    // clicks on the bubble must fall through to the world.
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                AddChild(_background);
            }

            // Style the panel background (re-applied on reuse: the corner radius scales with fontSize).
            var styleBox = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.15f, 0.15f, 0.85f),
                CornerRadiusTopLeft = Mathf.RoundToInt(6f * scale),
                CornerRadiusTopRight = Mathf.RoundToInt(6f * scale),
                CornerRadiusBottomLeft = Mathf.RoundToInt(6f * scale),
                CornerRadiusBottomRight = Mathf.RoundToInt(6f * scale),
            };
            _background.AddThemeStyleboxOverride("panel", styleBox);

            // Create label (NOT added to the tree yet — see below) or reuse the existing one.
            // For wrapped text the autowrap mode and wrap width must be set before it enters
            // the tree: once in the tree, a layout flush clamps its size to the min size cached
            // from whichever state the label was in when it entered — with autowrap still off
            // that min width is the full unwrapped text width, so the label is silently
            // stretched past the wrap width and the text renders on one line. (Verified with
            // tools/bubble_order_test.gd: pre-add setup stays stable, post-add gets clobbered.)
            // The same constraint bites on REUSE: in-tree autowrap OFF→ON (WordSmart) does not
            // reflow (stale line count and stale min-size), so a parented label is removed
            // before re-measuring and re-added below in its final state.
            if (_label == null)
            {
                _label = new Label
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    ZIndex = 21,
                    // The wrapped label rect is wider than the (text-hugging) background, so the
                    // transparent overhang must not steal mouse input.
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
            }
            _label.Text = message;
            _label.AddThemeFontSizeOverride("font_size", fontSize);
            _label.AddThemeConstantOverride("outline_size", Mathf.RoundToInt(4f * scale));
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
            if (_label.GetParent() != null)
                RemoveChild(_label);   // in-tree autowrap OFF→ON does not reflow (probed); the wrap
                                       // branch below or the final guarded add re-adds it in final state
            var font = _label.GetThemeFont("font") ?? _label.GetThemeDefaultFont();
            float textWidth;
            float textHeight;

            if (font == null)
            {
                // Effectively unreachable: the theme always provides a default font.
                textWidth = maxTextWidth;
                textHeight = 14f * scale;
            }
            else
            {
                Vector2 natural = font.GetStringSize(message, HorizontalAlignment.Left, -1, fontSize);
                if (natural.X <= maxTextWidth)
                {
                    // Fits on one line — shrink bubble to fit.
                    _label.AutowrapMode = TextServer.AutowrapMode.Off;   // reset for reuse: a previously-wrapped label
                                                                         // must not reflow the new shorter text
                    textWidth = natural.X;
                    textHeight = font.GetHeight(fontSize);
                }
                else
                {
                    // Wrapped: width hugs the longest line; height comes from the label itself.
                    _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    _label.Size = new Vector2(maxTextWidth, 0);
                    if (_label.GetParent() == null) AddChild(_label);
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
            var bgSize = textSize + padding * 2;
            BackgroundWidth = bgSize.X / scale;   // WORLD units (self-anchoring is world-unit)
            _bgScreen = bgSize;                   // screen px (ScreenBounds cull rect)
            _background.Size = bgSize;
            _background.Position = new Vector2(0, -bgSize.Y);

            // Size the label to the measured text block, inset by the padding. For wrapped
            // text the label keeps the full max text width (not the longest line) so its
            // internal reflow matches the measurement above exactly; the extra width is a
            // transparent overhang beyond the background. (In the wrapped case the label was
            // already added during measurement — see above.)
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
