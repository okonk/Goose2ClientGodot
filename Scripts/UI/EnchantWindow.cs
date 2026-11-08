using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

public partial class EnchantWindow : BaseWindow, IWindow
{
    protected override bool DefaultVisible => false;

    public int WindowId { get; private set; }
    public WindowFrames WindowFrame => WindowFrames.Enchant;

    private CustomWindowSlot _itemSlot = null!;
    private CustomWindowSlot _essenceSlot = null!;
    private CustomWindowSlot _protectionSlot = null!;
    private VBoxContainer _lines = null!;
    private Label _placeholder = null!;
    private Button _enchantButton = null!;
    private bool _listenersRegistered;
    private bool[]? _mkwButtons;
    private EnchantWindowState _state = new();

    public override void _Ready()
    {
        base._Ready();

        Visible = false;

        _itemSlot = GetNode<CustomWindowSlot>("Content/ItemSlot");
        _essenceSlot = GetNode<CustomWindowSlot>("Content/EssenceSlot");
        _protectionSlot = GetNode<CustomWindowSlot>("Content/ProtectionSlot");
        _itemSlot.OnDrop = d => OnSlotDrop(_itemSlot, EnchantSlotRole.Item, d);
        _essenceSlot.OnDrop = d => OnSlotDrop(_essenceSlot, EnchantSlotRole.Essence, d);
        _protectionSlot.OnDrop = d => OnSlotDrop(_protectionSlot, EnchantSlotRole.Protection, d);

        _lines = GetNode<VBoxContainer>("Content/InfoPanel/Scroll/Lines");
        _placeholder = GetNode<Label>("Content/InfoPanel/Placeholder");

        _enchantButton = GetNode<Button>("Content/EnchantButton");
        _enchantButton.Visible = false;
        _enchantButton.Pressed += EnchantPressed;

        GameManager.Instance.PacketManager.Listen<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Listen<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Listen<CloseWindowPacket>(OnCloseWindow);
        GameManager.Instance.PacketManager.Listen<EnchantPreviewPacket>(OnEnchantPreview);
        _listenersRegistered = true;

        ScaleRegister();
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Remove<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Remove<CloseWindowPacket>(OnCloseWindow);
        GameManager.Instance.PacketManager.Remove<EnchantPreviewPacket>(OnEnchantPreview);
    }

    private void OnMakeWindow(object o)
    {
        var p = (MakeWindowPacket)o;
        if (p.WindowFrame != WindowFrames.Enchant) return;
        if (p.WindowId != WindowId)
            ResetAll();
        _mkwButtons = p.Buttons;
        Visible = true;
        Title = p.Title;
        WindowId = p.WindowId;
        _state.WindowId = WindowId;
        Refresh();
    }

    private void OnEndWindow(object o)
    {
        var p = (EndWindowPacket)o;
        if (p.WindowId == WindowId) Visible = true;
    }

    private void OnCloseWindow(object o)
    {
        var p = (CloseWindowPacket)o;
        if (p.WindowId != WindowId) return;
        Visible = false;
        ResetAll();
    }

    private void OnEnchantPreview(object o)
    {
        var p = (EnchantPreviewPacket)o;
        _state.Apply(p);
        SyncIcons();
        RefreshLines();
        Refresh();
    }

    private void OnSlotDrop(CustomWindowSlot target, EnchantSlotRole role, Godot.Collections.Dictionary data)
    {
        if (!data.TryGetValue("kind", out var kind) || kind.AsString() != "item") return;
        if (!data.TryGetValue("slot", out var slotVal)) return;

        if (SlotDropRouting.AsOrDefault<ItemSlot>(slotVal) is { } src)
        {
            if (!CustomWindowValidation.IsInventorySource(src.Window)) return;
            if (!src.HasItem) return;
            var triple = _state.Place(role, src.SlotNumber + 1);
            SendTriple(triple);
            SyncIcons();
            Refresh();
            return;
        }

        if (SlotDropRouting.AsOrDefault<CustomWindowSlot>(slotVal) is { } csrc)
        {
            if (csrc == target)
            {
                var cleared = _state.Clear(role);
                SendTriple(cleared);
                SyncIcons();
                Refresh();
                return;
            }
            if (!csrc.HasItem) return;
            var moved = _state.Place(role, csrc.SlotId);
            SendTriple(moved);
            SyncIcons();
            Refresh();
        }
    }

    private void EnchantPressed()
    {
        var triple = _state.ConfirmTriple();
        GameManager.Instance.NetworkClient.EnchantWindowConfirm(triple.Item, triple.Essence, triple.Protection);
        Refresh();
    }

    private void SendTriple(EnchantTriple triple)
    {
        GameManager.Instance.NetworkClient.EnchantWindowSlots(triple.Item, triple.Essence, triple.Protection);
    }

    private void SyncIcons()
    {
        SyncIcon(_itemSlot, _state.ItemSlot);
        SyncIcon(_essenceSlot, _state.EssenceSlot);
        SyncIcon(_protectionSlot, _state.ProtectionSlot);
    }

    private void SyncIcon(CustomWindowSlot slot, int id)
    {
        if (id == 0)
        {
            slot.ClearItem();
            slot.SlotId = 0;
            return;
        }
        var stats = GameManager.Instance.Hud?.Inventory.GetSlot(id - 1);
        if (stats == null)
        {
            slot.ClearItem();
            slot.SlotId = 0;
            return;
        }
        slot.SetItem(stats);
        slot.SlotId = id;
    }

    private void RefreshLines()
    {
        foreach (var child in _lines.GetChildren())
            child.QueueFree();
        foreach (var line in _state.Lines)
            _lines.AddChild(new Label
            {
                Text = line,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            });
        _placeholder.Visible = _state.Lines.Length == 0;
    }

    private void Refresh()
    {
        _enchantButton.Visible = WindowButtonFlags.IsEnabled(_mkwButtons, WindowButtons.OK);
        _enchantButton.Disabled = !_state.CanConfirm;
    }

    private void ResetAll()
    {
        _state.Reset();
        WindowId = 0;
        _mkwButtons = null;
        SyncIcons();
        RefreshLines();
        Refresh();
    }

    protected override void OnClosePressed()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Close, WindowId, 0);
        ResetAll();
        Hide();
    }
}
