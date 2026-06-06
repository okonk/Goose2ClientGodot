using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

/// <summary>
/// Inventory window — 30-slot grid with gold display, double-click use, drag-and-drop.
/// </summary>
public partial class InventoryWindow : BaseWindow, IWindow
{
    public const int InventorySize = 30;

    private static readonly PackedScene SlotScene = GD.Load<PackedScene>("res://Scenes/UI/ItemSlot.tscn");

    private ItemSlot[] _slots;
    private Label _goldText;
    private bool _listenersRegistered;

    public int WindowId => (int)WindowFrame;
    public WindowFrames WindowFrame => WindowFrames.Inventory;

    public override void _Ready()
    {
        base._Ready();

        _goldText = GetNode<Label>("Content/GoldText");

        _slots = new ItemSlot[InventorySize];
        var grid = GetNode<GridContainer>("Content/SlotGrid");

        for (int i = 0; i < InventorySize; i++)
        {
            var slot = SlotScene.Instantiate<ItemSlot>();
            grid.AddChild(slot);
            slot.SlotNumber = i;
            slot.Window = this;
            slot.OnDoubleClick = UseItem;
            slot.OnDropItem = DropItem;
            _slots[i] = slot;
        }

        GameManager.Instance.PacketManager.Listen<InventorySlotPacket>(OnInventorySlot);
        GameManager.Instance.PacketManager.Listen<ClearInventorySlotPacket>(OnClearInventorySlot);
        GameManager.Instance.PacketManager.Listen<StatusInfoPacket>(OnStatusInfo);
        _listenersRegistered = true;
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<InventorySlotPacket>(OnInventorySlot);
        GameManager.Instance.PacketManager.Remove<ClearInventorySlotPacket>(OnClearInventorySlot);
        GameManager.Instance.PacketManager.Remove<StatusInfoPacket>(OnStatusInfo);
    }

    private void OnStatusInfo(object o)
    {
        var p = (StatusInfoPacket)o;
        _goldText.Text = p.Gold.ToString("N0");
    }

    private void OnInventorySlot(object o)
    {
        var p = (InventorySlotPacket)o;
        if (p.SlotNumber < 0 || p.SlotNumber >= _slots.Length) return;
        _slots[p.SlotNumber].SetItem(ItemStats.FromPacket(p));
    }

    private void OnClearInventorySlot(object o)
    {
        var p = (ClearInventorySlotPacket)o;
        if (p.SlotNumber < 0 || p.SlotNumber >= _slots.Length) return;
        _slots[p.SlotNumber].ClearItem();
    }

    public void UseItem(ItemStats stats)
    {
        GameManager.Instance.NetworkClient.UseItem(stats.SlotNumber);
    }

    private void DropItem(IWindow fromWindow, int fromSlot, int toSlot)
    {
        if (fromWindow.WindowId == WindowId)
        {
            var item = _slots[fromSlot].Stats;
            int amount = Helpers.GetStackSplitAmount(item.StackSize);
            if (item.StackSize == amount)
                GameManager.Instance.NetworkClient.MoveItemInInventory(fromSlot, toSlot);
            else
                GameManager.Instance.NetworkClient.SplitStackInInventory(fromSlot, toSlot, amount);
        }
        else if (fromWindow.WindowFrame == WindowFrames.Vendor && fromWindow is INpcWindow npc)
        {
            GameManager.Instance.NetworkClient.VendorPurchaseItem(npc.NpcId, fromSlot);
        }
        else
        {
            GameManager.Instance.NetworkClient.MoveWindowToInventory(fromWindow.WindowId, fromSlot, toSlot);
        }
    }

    public ItemStats GetSlot(int slotNumber) => _slots[slotNumber].Stats;
}
