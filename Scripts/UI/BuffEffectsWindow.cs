using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI
{
    /// <summary>
    /// Buff effects window — 20-slot horizontal row of buff icons.
    /// Double-clicking a buff kills it.
    /// </summary>
    public partial class BuffEffectsWindow : Control
    {
        public const int SlotCount = 20;

        private static readonly PackedScene SlotScene = GD.Load<PackedScene>("res://Scenes/UI/BuffEffect.tscn");

        private BuffEffect[] _slots;
        private bool _listenersRegistered;

        public override void _Ready()
        {
            _slots = new BuffEffect[SlotCount];
            var row = GetNode<HBoxContainer>("SlotRow");

            for (int i = 0; i < SlotCount; i++)
            {
                var slot = SlotScene.Instantiate<BuffEffect>();
                row.AddChild(slot);
                slot.SlotNumber = i;
                slot.OnDoubleClick = KillBuff;
                _slots[i] = slot;
            }

            GameManager.Instance.PacketManager.Listen<BuffBarPacket>(OnBuffBar);
            _listenersRegistered = true;
        }

        public override void _ExitTree()
        {
            if (!_listenersRegistered) return;
            GameManager.Instance.PacketManager.Remove<BuffBarPacket>(OnBuffBar);
        }

        private void OnBuffBar(object o)
        {
            var p = (BuffBarPacket)o;
            if (p.SlotNumber < 0 || p.SlotNumber >= _slots.Length) return;
            _slots[p.SlotNumber].SetEffect(p);
        }

        public void KillBuff(int index)
        {
            GameManager.Instance.NetworkClient.KillBuff(index + 1);
        }
    }
}
