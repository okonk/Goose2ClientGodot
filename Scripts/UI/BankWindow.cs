using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

/// <summary>
/// Bank window — 30-slot server-spawned bank with Back/Next paging.
/// Hidden until a MakeWindow/EndWindow pair for this frame arrives.
/// </summary>
public partial class BankWindow : BaseWindow, IWindow
{
    public const int BankSlots = 30;

    private static readonly PackedScene SlotScene = GD.Load<PackedScene>("res://Scenes/UI/ItemSlot.tscn");

    private ItemSlot[] _slots;
    private Button _backButton;
    private Button _nextButton;
    private bool _listenersRegistered;

    public int WindowId { get; private set; }
    public WindowFrames WindowFrame => WindowFrames.Bank;
    public int NpcId { get; private set; }

    public override void _Ready()
    {
        base._Ready();

        // Server-spawned — hidden until a MakeWindow/EndWindow for this frame arrives.
        Visible = false;

        // Resolve paging buttons — hidden until MakeWindow.Buttons says otherwise.
        _backButton = GetNode<Button>("Content/BackButton");
        _nextButton = GetNode<Button>("Content/NextButton");
        _backButton.Visible = false;
        _nextButton.Visible = false;
        _backButton.Pressed += BackClicked;
        _nextButton.Pressed += NextClicked;

        // Create bank slots programmatically
        _slots = new ItemSlot[BankSlots];
        var grid = GetNode<GridContainer>("Content/SlotGrid");

        for (int i = 0; i < BankSlots; i++)
        {
            var slot = SlotScene.Instantiate<ItemSlot>();
            grid.AddChild(slot);
            slot.SlotNumber = i;
            slot.Window = this;
            slot.OnDropItem = DropItem;
            _slots[i] = slot;
        }

        GameManager.Instance.PacketManager.Listen<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Listen<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Listen<BankSlotPacket>(OnBankSlot);
        GameManager.Instance.PacketManager.Listen<ClearBankSlotPacket>(OnClearBankSlot);
        _listenersRegistered = true;
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Remove<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Remove<BankSlotPacket>(OnBankSlot);
        GameManager.Instance.PacketManager.Remove<ClearBankSlotPacket>(OnClearBankSlot);
    }

    private void OnMakeWindow(object o)
    {
        var p = (MakeWindowPacket)o;
        if (p.WindowFrame != WindowFrames.Bank) return;
        NpcId = p.NpcId;
        Title = p.Title;
        WindowId = p.WindowId;

        // Bottom Back/Next visibility comes from MakeWindow.Buttons (Goose2 enum).
        _backButton.Visible = WindowButtonFlags.IsEnabled(p.Buttons, WindowButtons.Back);
        _nextButton.Visible = WindowButtonFlags.IsEnabled(p.Buttons, WindowButtons.Next);
    }

    private void OnEndWindow(object o)
    {
        var p = (EndWindowPacket)o;
        if (p.WindowId == WindowId) Visible = true;
    }

    private void OnBankSlot(object o)
    {
        var p = (BankSlotPacket)o;
        if (p.SlotNumber < 0 || p.SlotNumber >= _slots.Length) return;
        _slots[p.SlotNumber].SetItem(ItemStats.FromPacket(p));
    }

    private void OnClearBankSlot(object o)
    {
        var p = (ClearBankSlotPacket)o;
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

    public void NextClicked()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Next, WindowId, NpcId);
    }

    public void BackClicked()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Back, WindowId, NpcId);
    }

    protected override void OnClosePressed()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Close, WindowId, NpcId);
        Hide();
    }
}
