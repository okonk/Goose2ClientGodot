using System.Collections.Generic;
using Godot;
using Goose2Client;
using Goose2Client.Character;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

public partial class StylistWindow : BaseWindow, IWindow
{
    protected override bool DefaultVisible => false;

    public int WindowId { get; private set; }
    public WindowFrames WindowFrame => WindowFrames.Stylist;

    private TabBar _tabs = null!;
    private GridContainer _grid = null!;
    private CustomPreviewControl _preview = null!;
    private Label _costLabel = null!;
    private Button _resetButton = null!;
    private Button _changeButton = null!;
    private bool _listenersRegistered;
    private Color _hairColor;
    private readonly Dictionary<int, Button> _cells = new();
    private StylistWindowState _state = new();

    public override void _Ready()
    {
        base._Ready();

        Visible = false;

        _tabs = GetNode<TabBar>("Content/Tabs");
        _grid = GetNode<GridContainer>("Content/Scroll/Grid");
        _preview = GetNode<CustomPreviewControl>("Content/Preview");
        _costLabel = GetNode<Label>("Content/CostLabel");
        _resetButton = GetNode<Button>("Content/ResetButton");
        _changeButton = GetNode<Button>("Content/ChangeButton");
        GetNode<Button>("Content/RotateLeft").Pressed += () => Rotate(false);
        GetNode<Button>("Content/RotateRight").Pressed += () => Rotate(true);
        _tabs.TabChanged += _ =>
        {
            _state.Tab = (StylistTab)_tabs.CurrentTab;
            BuildGrid();
            Sync();
        };
        _resetButton.Pressed += () =>
        {
            _state.Reset();
            Sync();
        };
        _changeButton.Pressed += OnChangePressed;

        _preview.HideSlot(CharacterSlot.Helm);

        GameManager.Instance.PacketManager.Listen<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Listen<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Listen<CloseWindowPacket>(OnCloseWindow);
        GameManager.Instance.PacketManager.Listen<StatusInfoPacket>(OnStatusInfo);
        GameManager.Instance.CharacterUpdated += OnCharacterUpdated;
        _listenersRegistered = true;

        ScaleRegister();
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Remove<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Remove<CloseWindowPacket>(OnCloseWindow);
        GameManager.Instance.PacketManager.Remove<StatusInfoPacket>(OnStatusInfo);
        GameManager.Instance.CharacterUpdated -= OnCharacterUpdated;
    }

    public override void Relayout()
    {
        base.Relayout();
        _preview.Refresh();
    }

    private void OnMakeWindow(object o)
    {
        var p = (MakeWindowPacket)o;
        if (p.WindowFrame != WindowFrames.Stylist) return;
        var player = GameManager.Instance.CurrentMapManager?.LocalPlayer;
        if (player == null) return;
        var appearance = player.GetAppearance();
        _state.Open(appearance.HairId, appearance.FaceId);
        _hairColor = appearance.HairColor;
        _preview.SetFacing(player.Facing);
        for (int i = 0; i < 3 && _state.Facing != player.Facing; i++)
            _state.RotateRight();
        WindowId = p.WindowId;
        Title = p.Title;
        _tabs.CurrentTab = (int)_state.Tab;
        BuildGrid();
        Visible = true;
        Sync();
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
        CloseLocal();
    }

    private void OnStatusInfo(object o)
    {
        _state.Gold = ((StatusInfoPacket)o).Gold;
        if (Visible) Sync();
    }

    private void OnCharacterUpdated(Character.Character c)
    {
        if (!Visible || c != GameManager.Instance.CurrentMapManager?.LocalPlayer) return;
        var appearance = c.GetAppearance();
        _state.SetCurrent(appearance.HairId, appearance.FaceId);
        _hairColor = appearance.HairColor;
        if (_state.Tab == StylistTab.Hair)
            BuildGrid();
        Sync();
    }

    private void OnChangePressed()
    {
        if (!_state.CanChange) return;
        GameManager.Instance.NetworkClient.StylistChange(_state.SelectedHair, _state.SelectedFace);
    }

    private void Rotate(bool right)
    {
        if (right) _state.RotateRight();
        else _state.RotateLeft();
        _preview.SetFacing(_state.Facing);
    }

    private void CloseLocal()
    {
        Visible = false;
        _preview.ClearSlotGraphics();
        _preview.SetFacing(null);
        WindowId = 0;
    }

    protected override void OnClosePressed()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Close, WindowId, 0);
        CloseLocal();
    }

    private void BuildGrid()
    {
        foreach (var child in _grid.GetChildren())
            child.QueueFree();
        _cells.Clear();

        bool hair = _state.Tab == StylistTab.Hair;
        var slot = hair ? CharacterSlot.Hair : CharacterSlot.Eyes;
        var ids = hair ? StylistCatalog.HairIds : StylistCatalog.FaceIds;
        foreach (var id in ids)
        {
            var icon = new TextureRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            };
            icon.SetAnchorsPreset(LayoutPreset.FullRect);
            icon.OffsetLeft = 8f;
            icon.OffsetTop = 8f;
            icon.OffsetRight = -8f;
            icon.OffsetBottom = -8f;
            var tex = StylistIcon.Build(slot, id);
            icon.Texture = tex;
            if (hair)
                TintMaterial.Apply(icon, tex, _hairColor);

            var button = new Button
            {
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(64, 64)
            };
            button.AddChild(icon);
            button.Pressed += () =>
            {
                _state.Select(id);
                Sync();
            };
            _grid.AddChild(button);
            _cells[id] = button;
        }
        RefreshHighlights();
    }

    private void RefreshHighlights()
    {
        bool hair = _state.Tab == StylistTab.Hair;
        var selected = hair ? _state.SelectedHair : _state.SelectedFace;
        var current = hair ? _state.CurrentHair : _state.CurrentFace;
        foreach (var (id, button) in _cells)
        {
            button.Modulate = id == selected ? new Color(1f, 0.95f, 0.55f) : Colors.White;
            foreach (var state in new[] { "normal", "hover", "pressed" })
            {
                if (id == current && state == "normal")
                {
                    var border = new StyleBoxFlat
                    {
                        BorderColor = new Color(1f, 0.8f, 0.2f),
                        BorderWidthLeft = 2,
                        BorderWidthRight = 2,
                        BorderWidthTop = 2,
                        BorderWidthBottom = 2
                    };
                    button.AddThemeStyleboxOverride(state, border);
                }
                else
                    button.RemoveThemeStyleboxOverride(state);
            }
        }
    }

    private void Sync()
    {
        _preview.SetSlotGraphic(CharacterSlot.Hair, _state.SelectedHair);
        _preview.SetSlotGraphic(CharacterSlot.Eyes, _state.SelectedFace);
        _costLabel.Text = $"Cost: {_state.Cost:N0}";
        _costLabel.Modulate = _state.CanAfford ? Colors.White : new Color(1f, 0.45f, 0.45f);
        _changeButton.Disabled = !_state.CanChange;
        _resetButton.Disabled = _state.Cost == 0;
        RefreshHighlights();
    }
}
