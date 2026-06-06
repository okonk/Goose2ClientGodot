using Godot;
using Goose2Client.Network.Packets;
using System;

namespace Goose2Client.UI
{
    /// <summary>
    /// Single buff slot — displays an icon, shows tooltip on hover,
    /// and fires OnDoubleClick on double-click.
    /// </summary>
    public partial class BuffEffect : Panel
    {
        private TextureRect _icon;

        private string _effectName;

        public int SlotNumber { get; set; }
        public Action<int> OnDoubleClick { get; set; }

        public override void _Ready()
        {
            _icon = GetNode<TextureRect>("Icon");

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
            Goose2Client.UI.Icon.Apply(_icon, packet.GraphicFile, packet.GraphicId, 0, 0, 0, 0);
        }

        public void ClearEffect()
        {
            _effectName = null;
            Goose2Client.UI.Icon.Clear(_icon);
        }

        private void OnMouseEntered()
        {
            if (_effectName != null)
                TooltipManager.Instance.ShowTextTooltip(_effectName, this);
        }

        private void OnMouseExited()
        {
            TooltipManager.Instance.HideTextTooltip();
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
