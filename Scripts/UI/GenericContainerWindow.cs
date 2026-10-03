using System;
using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

public partial class GenericContainerWindow : BaseWindow, IWindow
{
    protected override bool DefaultVisible => false;

    public const int GenericContainerSlots = 30;

    private static readonly PackedScene SlotScene = GD.Load<PackedScene>("res://Scenes/UI/ItemSlot.tscn");

    private ItemSlot[] _slots = Array.Empty<ItemSlot>();
    private Button _backButton = null!;
    private Button _nextButton = null!;
    private bool _listenersRegistered;

    public int WindowId { get; private set; }
    public WindowFrames WindowFrame => WindowFrames.GenericContainer;
    public int NpcId { get; private set; }

    public override void _Ready()
    {
        base._Ready();

        Visible = false;

        _backButton = GetNode<Button>("Content/BackButton");
        _nextButton = GetNode<Button>("Content/NextButton");
        _backButton.Visible = false;
        _nextButton.Visible = false;
        _backButton.Pressed += BackClicked;
        _nextButton.Pressed += NextClicked;

        _slots = new ItemSlot[GenericContainerSlots];
        var grid = GetNode<GridContainer>("Content/SlotGrid");

        for (int i = 0; i < GenericContainerSlots; i++)
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
        GameManager.Instance.PacketManager.Listen<GenericWindowSlotPacket>(OnGenericWindowSlot);
        GameManager.Instance.PacketManager.Listen<ClearGenericWindowSlotPacket>(OnClearGenericWindowSlot);
        _listenersRegistered = true;

        UiScaleApplier.Instance!.ApplyFontSize(GetNode<Label>("TitleBar/TitleLabel"), 9);
        ScaleRegister();
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Remove<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Remove<GenericWindowSlotPacket>(OnGenericWindowSlot);
        GameManager.Instance.PacketManager.Remove<ClearGenericWindowSlotPacket>(OnClearGenericWindowSlot);
    }

    private void OnMakeWindow(object o)
    {
        var p = (MakeWindowPacket)o;
        if (p.WindowFrame != WindowFrames.GenericContainer) return;
        NpcId = p.NpcId;
        Title = p.Title;
        WindowId = p.WindowId;

        _backButton.Visible = WindowButtonFlags.IsEnabled(p.Buttons, WindowButtons.Back);
        _nextButton.Visible = WindowButtonFlags.IsEnabled(p.Buttons, WindowButtons.Next);
    }

    private void OnEndWindow(object o)
    {
        var p = (EndWindowPacket)o;
        if (p.WindowId == WindowId) Visible = true;
    }

    private void OnGenericWindowSlot(object o)
    {
        var p = (GenericWindowSlotPacket)o;
        if (p.WindowId != WindowId) return;
        if (p.SlotNumber < 0 || p.SlotNumber >= _slots.Length) return;
        _slots[p.SlotNumber].SetItem(ItemStats.FromPacket(p));
    }

    private void OnClearGenericWindowSlot(object o)
    {
        var p = (ClearGenericWindowSlotPacket)o;
        if (p.WindowId != WindowId) return;
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

    protected override bool AcceptsItemDrops => true;

    protected override void OnItemDropOnWindow(ItemSlot source, Vector2 globalPosition)
    {
        int toSlot = NearestSlot(_slots, globalPosition);
        if (toSlot >= 0)
            DropItem(source.Window, source.SlotNumber, toSlot);
    }
}
