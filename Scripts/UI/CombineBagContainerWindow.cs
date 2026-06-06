using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

/// <summary>
/// Combine bag window — 10-slot server-spawned combine bag.
/// Hidden until an EndWindow packet for this frame arrives.
/// Does not listen to MakeWindowPacket (server sends a fixed WindowId=22).
/// </summary>
public partial class CombineBagContainerWindow : BaseWindow, IWindow
{
    public const int SlotCount = 10;

    private static readonly PackedScene SlotScene = GD.Load<PackedScene>("res://Scenes/UI/ItemSlot.tscn");

    private ItemSlot[] _slots;
    private Button _combineButton;
    private bool _listenersRegistered;

    public int WindowId => 22;
    public WindowFrames WindowFrame => WindowFrames.TenSlot;

    public override void _Ready()
    {
        base._Ready();

        // Server-spawned — hidden until an EndWindow for this frame arrives.
        Visible = false;

        // Resolve combine button
        _combineButton = GetNode<Button>("Content/CombineButton");
        _combineButton.Pressed += CombineClicked;

        // Create 10 slots programmatically
        _slots = new ItemSlot[SlotCount];
        var grid = GetNode<GridContainer>("Content/SlotGrid");

        for (int i = 0; i < SlotCount; i++)
        {
            var slot = SlotScene.Instantiate<ItemSlot>();
            grid.AddChild(slot);
            slot.SlotNumber = i;
            slot.Window = this;
            slot.OnDropItem = DropItem;
            _slots[i] = slot;
        }

        GameManager.Instance.PacketManager.Listen<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Listen<CombineBagSlotPacket>(OnCombineBagSlot);
        GameManager.Instance.PacketManager.Listen<ClearCombineBagSlotPacket>(OnClearCombineBagSlot);
        _listenersRegistered = true;
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Remove<CombineBagSlotPacket>(OnCombineBagSlot);
        GameManager.Instance.PacketManager.Remove<ClearCombineBagSlotPacket>(OnClearCombineBagSlot);
    }

    private void OnEndWindow(object o)
    {
        var p = (EndWindowPacket)o;
        if (p.WindowId == WindowId) Visible = true;
    }

    private void OnCombineBagSlot(object o)
    {
        var p = (CombineBagSlotPacket)o;
        if (p.SlotNumber < 0 || p.SlotNumber >= _slots.Length) return;
        _slots[p.SlotNumber].SetItem(ItemStats.FromPacket(p));
    }

    private void OnClearCombineBagSlot(object o)
    {
        var p = (ClearCombineBagSlotPacket)o;
        if (p.SlotNumber < 0 || p.SlotNumber >= _slots.Length) return;
        _slots[p.SlotNumber].ClearItem();
    }

    private void DropItem(IWindow fromWindow, int fromSlot, int toSlot)
    {
        if (fromWindow.WindowFrame == WindowFrames.Inventory)
        {
            GameManager.Instance.NetworkClient.MoveInventoryToWindow(fromSlot, WindowId, toSlot);
        }
        else if (fromWindow.WindowFrame != WindowFrames.Vendor)
        {
            GameManager.Instance.NetworkClient.MoveWindowToWindow(fromWindow.WindowId, fromSlot, WindowId, toSlot);
        }
    }

    public void CombineClicked()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Combine, WindowId, 0);
    }

    protected override void OnClosePressed()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Close, WindowId, 0);
        Hide();
    }
}
