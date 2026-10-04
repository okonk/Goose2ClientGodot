using System;
using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

public partial class CustomWindow : BaseWindow, IWindow
{
    protected override bool DefaultVisible => false;

    public int WindowId { get; private set; }
    public WindowFrames WindowFrame => WindowFrames.Custom;

    private CustomWindowSlot _lookSlot = null!;
    private CustomWindowSlot _statsSlot = null!;
    private CustomPreviewControl _preview = null!;
    private ColorPickerControl _picker = null!;
    private LineEdit _nameField = null!;
    private Button _createButton = null!;
    private bool _listenersRegistered;

    private int _lookInvSlotId;
    private int _statsInvSlotId;
    private ItemStats? _lookStats;
    private bool _pendingCws;
    private bool _cwcPending;
    private bool[]? _mkwButtons;
    private int _r = CustomWindowMetrics.DefaultR;
    private int _g = CustomWindowMetrics.DefaultG;
    private int _b = CustomWindowMetrics.DefaultB;
    private int _a = CustomWindowMetrics.DefaultA;

    public override void _Ready()
    {
        base._Ready();

        Visible = false;

        _lookSlot = GetNode<CustomWindowSlot>("Content/LookSlot");
        _statsSlot = GetNode<CustomWindowSlot>("Content/StatsSlot");
        _lookSlot.OnDrop = d => OnSlotDrop(_lookSlot, d);
        _statsSlot.OnDrop = d => OnSlotDrop(_statsSlot, d);

        _preview = GetNode<CustomPreviewControl>("Content/Preview");

        _picker = GetNode<ColorPickerControl>("Content/ColorPicker");
        _picker.SetColor(new Color(_r / 255f, _g / 255f, _b / 255f, _a / 255f));
        _picker.ColorChanged += OnPickerColorChanged;

        _nameField = GetNode<LineEdit>("Content/NameField");
        _nameField.TextChanged += OnNameTextChanged;

        _createButton = GetNode<Button>("Content/CreateButton");
        _createButton.Visible = false;
        _createButton.Pressed += CreatePressed;

        GameManager.Instance.PacketManager.Listen<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Listen<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Listen<CloseWindowPacket>(OnCloseWindow);
        GameManager.Instance.PacketManager.Listen<CustomWindowGraphicPacket>(OnCustomWindowGraphic);
        GameManager.Instance.PacketManager.Listen<ServerMessagePacket>(OnServerMessage);
        _listenersRegistered = true;

        ScaleRegister();
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Remove<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Remove<CloseWindowPacket>(OnCloseWindow);
        GameManager.Instance.PacketManager.Remove<CustomWindowGraphicPacket>(OnCustomWindowGraphic);
        GameManager.Instance.PacketManager.Remove<ServerMessagePacket>(OnServerMessage);
    }

    public override void Relayout()
    {
        base.Relayout();
        // CustomPreviewControl has no resize handling and lays out against its Size at
        // Refresh() time, so Relayout must re-run the layout after UI scale changes.
        _preview.Refresh();
        _picker.SyncHsl();
    }

    private void OnPickerColorChanged(Color c)
    {
        _r = Mathf.RoundToInt(c.R * 255f);
        _g = Mathf.RoundToInt(c.G * 255f);
        _b = Mathf.RoundToInt(c.B * 255f);
        _a = Mathf.RoundToInt(c.A * 255f);
        _preview.SetTint(_r, _g, _b, _a);
    }

    private void OnMakeWindow(object o)
    {
        var p = (MakeWindowPacket)o;
        if (p.WindowFrame != WindowFrames.Custom) return;
        _mkwButtons = p.Buttons;
        // HUD survives map swaps and the server discards windows without a CLW,
        // so a new WindowId means a fresh session that must not inherit state.
        if (p.WindowId != WindowId)
            ResetState();
        Visible = true;
        _picker.SyncHsl();
        Title = p.Title;
        WindowId = p.WindowId;
        _preview.Refresh();
        RefreshCreate();
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
        ResetState();
    }

    private void OnCustomWindowGraphic(object o)
    {
        var p = (CustomWindowGraphicPacket)o;
        // Responses arrive in order and each reflects the server's latest look slot,
        // so applying every CWG converges (the fixed protocol has no request id).
        if (_pendingCws) _pendingCws = false;
        var target = CustomWindowValidation.PreviewTarget(_lookStats);
        if (p.EquippedId > 0)
            _preview.SetCustomGraphic(target, p.EquippedId, p.Pose);
        else
            _preview.SetCustomGraphic(target, 0, p.Pose);
    }

    private void OnServerMessage(object o)
    {
        // Known limitation (fixed protocol): an unrelated server message inside the
        // pending window can clear a valid look slot; the player re-drops.
        if (_pendingCws)
        {
            _pendingCws = false;
            _lookSlot.ClearItem();
            _lookSlot.SlotId = 0;
            _lookInvSlotId = 0;
            _lookStats = null;
            _preview.SetCustomGraphic(null, 0, 0);
            RefreshCreate();
        }
        if (_cwcPending)
        {
            _cwcPending = false;
            RefreshCreate();
        }
    }

    private void OnSlotDrop(CustomWindowSlot target, Godot.Collections.Dictionary data)
    {
        if (!data.TryGetValue("kind", out var kind) || kind.AsString() != "item") return;
        if (!data.TryGetValue("slot", out var slotVal)) return;

        if (SlotDropRouting.AsOrDefault<ItemSlot>(slotVal) is { } src)
        {
            if (!CustomWindowValidation.IsInventorySource(src.Window)) return;
            if (!src.HasItem) return;
            var stats = src.Stats!;
            if (!CustomWindowValidation.IsValidCandidate(stats)) return;
            var other = target == _lookSlot ? _statsSlot : _lookSlot;
            if (other.HasItem && !CustomWindowValidation.TypesCompatible(stats, other.Stats!)) return;
            int id = src.SlotNumber + 1;
            SetSlot(target, id, stats);
            return;
        }

        if (SlotDropRouting.AsOrDefault<CustomWindowSlot>(slotVal) is { } csrc)
        {
            if (csrc == target)
            {
                ClearSlot(target);
                return;
            }
            var id = csrc.SlotId;
            if (!csrc.HasItem) return;
            var stats = csrc.Stats!;
            ClearSlot(csrc);
            SetSlot(target, id, stats);
        }
    }

    private void SetSlot(CustomWindowSlot target, int id, ItemStats stats)
    {
        bool lookChanged = target == _lookSlot && _lookInvSlotId != id;
        target.SetItem(stats);
        target.SlotId = id;
        if (target == _lookSlot)
        {
            _lookInvSlotId = id;
            _lookStats = stats;
            if (lookChanged)
                // Hide the replaced layer until the CWG arrives.
                _preview.SetCustomGraphic(CustomWindowValidation.PreviewTarget(stats) ?? null, 0, 0);
        }
        else
        {
            _statsInvSlotId = id;
        }
        AfterSlotChange();
    }

    private void ClearSlot(CustomWindowSlot target)
    {
        bool lookChanged = target == _lookSlot && _lookInvSlotId != 0;
        target.ClearItem();
        target.SlotId = 0;
        if (target == _lookSlot)
        {
            _lookInvSlotId = 0;
            _lookStats = null;
            if (lookChanged)
                // A look clear produces no CWG, so this is the only invalidation path.
                _preview.SetCustomGraphic(null, 0, 0);
        }
        else
        {
            _statsInvSlotId = 0;
        }
        AfterSlotChange();
    }

    private void AfterSlotChange()
    {
        GameManager.Instance.NetworkClient.CustomWindowSlots(_lookInvSlotId, _statsInvSlotId);
        // A CWS with look = 0 produces neither CWG nor an error, so only look-bearing
        // requests are trackable.
        _pendingCws = _lookInvSlotId != 0;
        RefreshCreate();
    }

    private void OnNameTextChanged(string text)
    {
        var filtered = text.Replace(",", "");
        if (filtered != text)
        {
            _nameField.Text = filtered;
            return;
        }
        RefreshCreate();
    }

    private void CreatePressed()
    {
        var name = _nameField.Text.Trim().Replace(",", "");
        _cwcPending = true;
        RefreshCreate();
        GameManager.Instance.NetworkClient.CustomWindowCreate(_lookInvSlotId, _statsInvSlotId, _r, _g, _b, _a, name);
    }

    private void RefreshCreate()
    {
        _createButton.Visible = WindowButtonFlags.IsEnabled(_mkwButtons, WindowButtons.OK);
        _createButton.Disabled = _cwcPending
            || _lookInvSlotId == 0
            || _statsInvSlotId == 0
            || string.IsNullOrWhiteSpace(_nameField.Text);
    }

    private void ResetState()
    {
        _lookSlot.ClearItem();
        _statsSlot.ClearItem();
        _lookSlot.SlotId = 0;
        _statsSlot.SlotId = 0;
        _lookInvSlotId = 0;
        _statsInvSlotId = 0;
        _lookStats = null;
        _pendingCws = false;
        _cwcPending = false;
        _r = CustomWindowMetrics.DefaultR;
        _g = CustomWindowMetrics.DefaultG;
        _b = CustomWindowMetrics.DefaultB;
        _a = CustomWindowMetrics.DefaultA;
        _nameField.Text = "";
        _picker.SetColor(new Color(_r / 255f, _g / 255f, _b / 255f, _a / 255f));
        _preview.SetTint(_r, _g, _b, _a);
        _preview.SetCustomGraphic(null, 0, 0);
        _preview.Refresh();
        RefreshCreate();
    }

    protected override void OnClosePressed()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Close, WindowId, 0);
        ResetState();
        Hide();
    }
}
