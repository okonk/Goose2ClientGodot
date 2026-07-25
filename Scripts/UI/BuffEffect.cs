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
        private string _effectName;
        private string _tooltipText;

        public int SlotNumber { get; set; }
        public Action<int> OnDoubleClick { get; set; }

        public override void _Ready()
        {
            CustomMinimumSize = SlotSize;
            _icon = GetNode<TextureRect>("Icon");
            // Empty slots must not steal mouse from the world / neighboring icons.
            MouseFilter = MouseFilterEnum.Ignore;

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
            // Protocol currently ships name only; keep duration optional for future packets.
            _tooltipText = BuildTooltip(packet.Name, durationText: null);
            Goose2Client.UI.Icon.Apply(_icon, packet.GraphicFile, packet.GraphicId, 0, 0, 0, 0);
            MouseFilter = MouseFilterEnum.Stop;
        }

        public void ClearEffect()
        {
            _effectName = null;
            _tooltipText = null;
            Goose2Client.UI.Icon.Clear(_icon);
            MouseFilter = MouseFilterEnum.Ignore;
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
