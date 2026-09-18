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

    private CustomWindowSlot _lookSlot;
    private CustomWindowSlot _statsSlot;
    private CustomPreviewControl _preview;
    private TextureRect _gradient;
    private TextureRect _cursor;
    private HSlider _rSlider;
    private HSlider _gSlider;
    private HSlider _bSlider;
    private HSlider _aSlider;
    private Label _rValue;
    private Label _gValue;
    private Label _bValue;
    private Label _aValue;
    private LineEdit _nameField;
    private Button _createButton;
    private bool _listenersRegistered;

    private int _lookInvSlotId;
    private int _statsInvSlotId;
    private ItemStats _lookStats;
    private bool _pendingCws;
    private bool _cwcPending;
    private bool[] _mkwButtons;
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

        _gradient = GetNode<TextureRect>("Content/Gradient");
        _cursor = GetNode<TextureRect>("Content/Gradient/Cursor");
        _gradient.GuiInput += OnGradientGuiInput;
        BuildGradientTexture();

        _rSlider = GetNode<HSlider>("Content/RSlider");
        _gSlider = GetNode<HSlider>("Content/GSlider");
        _bSlider = GetNode<HSlider>("Content/BSlider");
        _aSlider = GetNode<HSlider>("Content/ASlider");
        _rValue = GetNode<Label>("Content/RValue");
        _gValue = GetNode<Label>("Content/GValue");
        _bValue = GetNode<Label>("Content/BValue");
        _aValue = GetNode<Label>("Content/AValue");
        _rSlider.ValueChanged += v => { _r = (int)v; _rValue.Text = _r.ToString(); UpdateTint(); };
        _gSlider.ValueChanged += v => { _g = (int)v; _gValue.Text = _g.ToString(); UpdateTint(); };
        _bSlider.ValueChanged += v => { _b = (int)v; _bValue.Text = _b.ToString(); UpdateTint(); };
        _aSlider.ValueChanged += v => { _a = (int)v; _aValue.Text = _a.ToString(); UpdateTint(); };

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
    }

    private void BuildGradientTexture()
    {
        const int size = 128;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var c = CustomWindowMetrics.SampleGradient(new Vector2(x / (size - 1f), y / (size - 1f)));
                img.SetPixel(x, y, new Color(c.X / 255f, c.Y / 255f, c.Z / 255f, 1f));
            }
        }
        _gradient.Texture = ImageTexture.CreateFromImage(img);
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

        if (slotVal.As<ItemSlot>() is { } src)
        {
            if (!CustomWindowValidation.IsInventorySource(src.Window)) return;
            if (!CustomWindowValidation.IsValidCandidate(src.Stats)) return;
            var other = target == _lookSlot ? _statsSlot : _lookSlot;
            if (other.HasItem && !CustomWindowValidation.TypesCompatible(src.Stats, other.Stats)) return;
            int id = src.SlotNumber + 1;
            if (other.HasItem && other.SlotId == id) return;
            SetSlot(target, id, src.Stats);
            return;
        }

        if (slotVal.As<CustomWindowSlot>() is { } csrc)
        {
            if (csrc == target)
            {
                ClearSlot(target);
                return;
            }
            var id = csrc.SlotId;
            var stats = csrc.Stats;
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

    private void OnGradientGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
            ApplyGradientAt(mb.Position);
        else if (@event is InputEventMouseMotion mm && Input.IsMouseButtonPressed(MouseButton.Left))
            ApplyGradientAt(mm.Position);
    }

    private void ApplyGradientAt(Vector2 pos)
    {
        var n = new Vector2(
            Mathf.Clamp(pos.X / _gradient.Size.X, 0f, 1f),
            Mathf.Clamp(pos.Y / _gradient.Size.Y, 0f, 1f));
        var rgb = CustomWindowMetrics.SampleGradient(n);
        _rSlider.Value = rgb.X;
        _gSlider.Value = rgb.Y;
        _bSlider.Value = rgb.Z;
        _cursor.Position = pos - _cursor.Size / 2;
    }

    private void UpdateTint()
    {
        _preview.SetTint(_r, _g, _b, _a);
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
        _rSlider.Value = _r;
        _gSlider.Value = _g;
        _bSlider.Value = _b;
        _aSlider.Value = _a;
        _rValue.Text = _r.ToString();
        _gValue.Text = _g.ToString();
        _bValue.Text = _b.ToString();
        _aValue.Text = _a.ToString();
        _nameField.Text = "";
        _cursor.Position = Vector2.Zero;
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
