using Godot;
using Goose2Client.Network.Packets;
using System;

namespace Goose2Client.UI
{
    /// <summary>
    /// Single buff slot — displays an icon, shows tooltip on hover,
    /// and fires OnDoubleClick on double-click.
    /// Must keep a non-zero minimum size so HBoxContainer children don't collapse
    /// and stack on top of each other (which made only one buff icon visible).
    /// </summary>
    public partial class BuffEffect : Panel
    {
        public static readonly Vector2 SlotSize = new(20, 20);

        private TextureRect _icon;
        private BuffSweepBar _sweep;
        private Label _countdown;
        private string _effectName;
        private string _tooltipText;
        private long _remainingMs;
        private long _totalMs;
        private DateTimeOffset _expiresAt;
        private bool _hovering;

        internal static float BlinkAlpha(double nowSeconds) => Mathf.Clamp(0.65f + 0.35f * (float)Math.Sin(2 * Math.PI * nowSeconds), 0.3f, 1.0f);

        public int SlotNumber { get; set; }
        public Action<int> OnDoubleClick { get; set; }

        public override void _Ready()
        {
            CustomMinimumSize = SlotSize;
            _icon = GetNode<TextureRect>("Icon");
            _sweep = GetNode<BuffSweepBar>("Sweep");
            _countdown = GetNode<Label>("Countdown");
            UiScaleApplier.Instance.ApplyFontSize(_countdown, 10f);
            // Empty slots must not steal mouse from the world / neighboring icons.
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;

            MouseEntered += OnMouseEntered;
            MouseExited += OnMouseExited;
        }

        public void SetEffect(BuffBarPacket packet)
        {
            if (string.IsNullOrWhiteSpace(packet.Name))
            {
                ClearEffect();
                return;
            }

            // The packet carries the buff's *remaining* ms, so every full-bar resend is
            // self-describing and the deadline is simply reset from it. Total is the
            // sweep bar's denominator so a resend doesn't reset the bar to full; older
            // servers omit it, in which case remaining is both numerator and denominator.
            _effectName = packet.Name;
            _remainingMs = packet.RemainingMs;
            _totalMs = packet.TotalMs > 0 ? packet.TotalMs : _remainingMs;
            _sweep.Visible = _remainingMs > 0;
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(_remainingMs);
            _icon.Modulate = Colors.White;
            _tooltipText = BuildTooltip(packet.Name, _remainingMs / 1000.0);
            Goose2Client.UI.Icon.Apply(_icon, packet.GraphicFile, packet.GraphicId, 0, 0, 0, 0);
            MouseFilter = MouseFilterEnum.Stop;
            Visible = true;
        }

        public void ClearEffect()
        {
            _effectName = null;
            _hovering = false;
            _tooltipText = null;
            _remainingMs = 0;
            _totalMs = 0;
            _sweep.Visible = false;
            _countdown.Visible = false;
            _icon.Modulate = Colors.White;
            Goose2Client.UI.Icon.Clear(_icon);
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;
        }

        public override void _Process(double delta)
        {
            if (_effectName == null || _remainingMs <= 0)
                return;

            var remaining = (_expiresAt - DateTimeOffset.UtcNow).TotalSeconds;
            _sweep.Update(remaining, _totalMs / 1000.0, dangerSeconds: 10);

            var showCountdown = remaining < 30;
            if (_countdown.Visible != showCountdown)
                _countdown.Visible = showCountdown;
            if (showCountdown)
            {
                var text = FormatRemaining(Mathf.Max(0.0, remaining));
                if (text != _countdown.Text)
                    _countdown.Text = text;
            }

            _icon.Modulate = remaining <= 15
                ? new Color(1, 1, 1, BlinkAlpha(Time.GetTicksMsec() / 1000.0))
                : Colors.White;

            if (_hovering && TooltipManager.Instance != null)
            {
                var text = BuildTooltip(_effectName, Mathf.Max(0.0, remaining));
                if (text != _tooltipText)
                {
                    _tooltipText = text;
                    TooltipManager.Instance.ShowTextTooltip(text, this);
                }
            }
        }

        private static string FormatRemaining(double remainingSeconds)
        {
            if (remainingSeconds <= 0)
                return "0";
            if (remainingSeconds < 60)
                return CooldownOverlay.FormatCountdown(remainingSeconds) + "s";
            return CooldownOverlay.FormatCountdown(remainingSeconds);
        }

        private static string BuildTooltip(string name, double remainingSeconds)
        {
            var rs = TimeSpan.FromSeconds(Math.Max(0.0, remainingSeconds)).FormatDuration();
            if (rs.Length == 0)
                return name;
            return $"{name} ({rs} remaining)";
        }

        private void OnMouseEntered()
        {
            _hovering = true;
            if (_tooltipText == null || TooltipManager.Instance == null)
                return;
            TooltipManager.Instance.ShowTextTooltip(_tooltipText, this);
        }

        private void OnMouseExited()
        {
            _hovering = false;
            TooltipManager.Instance?.HideTextTooltip();
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mb &&
                mb.ButtonIndex == MouseButton.Left &&
                mb.DoubleClick &&
                _effectName != null)
            {
                OnDoubleClick?.Invoke(SlotNumber);
            }
        }
    }
}
