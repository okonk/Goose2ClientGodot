using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

public partial class GridContainerWindowManager : BaseMultipleWindowManager<GridContainerWindow>
{
    private bool _listenersRegistered;

    public override string PrefabPath => "res://Scenes/UI/GridContainerWindow.tscn";
    public override bool MatchesFrame(WindowFrames frame) => frame == WindowFrames.GridContainer;

    public override void _Ready()
    {
        base._Ready();
        GameManager.Instance.PacketManager.Listen<GenericWindowSlotPacket>(OnGenericWindowSlot);
        GameManager.Instance.PacketManager.Listen<ClearGenericWindowSlotPacket>(OnClearGenericWindowSlot);
        _listenersRegistered = true;
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<GenericWindowSlotPacket>(OnGenericWindowSlot);
        GameManager.Instance.PacketManager.Remove<ClearGenericWindowSlotPacket>(OnClearGenericWindowSlot);
    }

    private void OnGenericWindowSlot(object o)
    {
        var p = (GenericWindowSlotPacket)o;
        if (TryGetWindow(p.WindowId, out var w))
            w.SetSlot(p.SlotNumber, ItemStats.FromPacket(p));
    }

    private void OnClearGenericWindowSlot(object o)
    {
        var p = (ClearGenericWindowSlotPacket)o;
        if (TryGetWindow(p.WindowId, out var w))
            w.ClearSlot(p.SlotNumber);
    }
}
