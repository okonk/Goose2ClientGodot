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
        private CooldownOverlay _sweep;
        private string _effectName;
        private string _tooltipText;
        private long _durationMs;
        private DateTimeOffset _expiresAt;

        internal static float BlinkAlpha(double nowSeconds) => Mathf.Clamp(0.65f + 0.35f * (float)Math.Sin(2 * Math.PI * nowSeconds), 0.3f, 1.0f);

        public int SlotNumber { get; set; }
        public Action<int> OnDoubleClick { get; set; }

        public override void _Ready()
        {
            CustomMinimumSize = SlotSize;
            _icon = GetNode<TextureRect>("Icon");
            _sweep = GetNode<CooldownOverlay>("Sweep");
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

            _effectName = packet.Name;
            _durationMs = packet.DurationMs;
            _sweep.Visible = _durationMs > 0;
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(_durationMs);
            _icon.Modulate = Colors.White;
            _tooltipText = BuildTooltip(packet.Name, _durationMs > 0 ? CooldownOverlay.FormatCountdown(_durationMs / 1000.0) : null);
            Goose2Client.UI.Icon.Apply(_icon, packet.GraphicFile, packet.GraphicId, 0, 0, 0, 0);
            MouseFilter = MouseFilterEnum.Stop;
            Visible = true;
        }

        public void ClearEffect()
        {
            _effectName = null;
            _tooltipText = null;
            _durationMs = 0;
            _sweep.Visible = false;
            _icon.Modulate = Colors.White;
            Goose2Client.UI.Icon.Clear(_icon);
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;
        }

        public override void _Process(double delta)
        {
            if (_effectName == null || _durationMs <= 0)
                return;

            var remaining = (_expiresAt - DateTimeOffset.UtcNow).TotalSeconds;
            _sweep.Update(remaining, _durationMs / 1000.0, growthMode: true, dangerSeconds: 10);

            _icon.Modulate = remaining <= 15
                ? new Color(1, 1, 1, BlinkAlpha(Time.GetTicksMsec() / 1000.0))
                : Colors.White;
        }

        private static string BuildTooltip(string name, string durationText)
        {
            if (string.IsNullOrWhiteSpace(durationText))
                return name;
            return $"{name}\n{durationText}";
        }

        private void OnMouseEntered()
        {
            if (_tooltipText == null || TooltipManager.Instance == null)
                return;
            TooltipManager.Instance.ShowTextTooltip(_tooltipText, this);
        }

        private void OnMouseExited()
        {
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
