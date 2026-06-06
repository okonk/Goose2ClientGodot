using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

/// <summary>
/// Vendor window — 40-slot server-spawned shop grid.
/// Hidden until a MakeWindow/EndWindow pair for this frame arrives.
/// </summary>
public partial class VendorWindow : BaseWindow, IWindow, INpcWindow
{
    public const int VendorSlots = 40;

    private static readonly PackedScene SlotScene = GD.Load<PackedScene>("res://Scenes/UI/ItemSlot.tscn");

    private ItemSlot[] _slots;
    private bool _listenersRegistered;

    public int WindowId { get; private set; }
    public WindowFrames WindowFrame => WindowFrames.Vendor;
    public int NpcId { get; private set; }

    public override void _Ready()
    {
        base._Ready();

        // Server-spawned — hidden until a MakeWindow/EndWindow for this frame arrives.
        Visible = false;

        _slots = new ItemSlot[VendorSlots];
        var grid = GetNode<GridContainer>("Content/SlotGrid");

        for (int i = 0; i < VendorSlots; i++)
        {
            var slot = SlotScene.Instantiate<ItemSlot>();
            grid.AddChild(slot);
            slot.SlotNumber = i;
            slot.Window = this;
            slot.OnDoubleClick = BuyItem;
            slot.OnDropItem = DropItem;
            _slots[i] = slot;
        }

        GameManager.Instance.PacketManager.Listen<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Listen<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Listen<VendorSlotPacket>(OnVendorSlot);
        GameManager.Instance.PacketManager.Listen<ClearVendorPacket>(OnClearVendor);
        _listenersRegistered = true;
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Remove<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Remove<VendorSlotPacket>(OnVendorSlot);
        GameManager.Instance.PacketManager.Remove<ClearVendorPacket>(OnClearVendor);
    }

    private void OnMakeWindow(object o)
    {
        var p = (MakeWindowPacket)o;
        if (p.WindowFrame != WindowFrames.Vendor) return;
        NpcId = p.NpcId;
        Title = p.Title;
        WindowId = p.WindowId;
    }

    private void OnEndWindow(object o)
    {
        var p = (EndWindowPacket)o;
        if (p.WindowId == WindowId) Visible = true;
    }

    private void OnVendorSlot(object o)
    {
        var p = (VendorSlotPacket)o;
        if (p.SlotNumber < 0 || p.SlotNumber >= _slots.Length) return;
        _slots[p.SlotNumber].SetItem(ItemStats.FromPacket(p));
    }

    private void OnClearVendor(object o)
    {
        foreach (var s in _slots) s.ClearItem();
    }

    public void BuyItem(ItemStats stats)
    {
        GameManager.Instance.NetworkClient.VendorPurchaseItem(NpcId, stats.SlotNumber);
    }

    private void DropItem(IWindow fromWindow, int fromSlot, int toSlot)
    {
        if (fromWindow is InventoryWindow inv)
        {
            var fromItem = inv.GetSlot(fromSlot);
            if (fromItem != null)
                GameManager.Instance.NetworkClient.VendorSellItem(NpcId, fromSlot, Helpers.GetStackSplitAmount(fromItem.StackSize));
        }
    }

    protected override void OnClosePressed()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Close, WindowId, NpcId);
        Hide();
    }
}
