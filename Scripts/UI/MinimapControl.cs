using System.Collections.Generic;
using Godot;
using Goose2Client.Map;

namespace Goose2Client.UI;

/// <summary>
/// Framed minimap: this control draws the themed border, an inset clipped child draws the
/// map, and a label under the frame shows the map name.
/// </summary>
public partial class MinimapControl : Control, IScalableWindow
{
    public const int WindowTiles = 64;
    public const int BasePixels = 128;
    private const int BaseBorder = 2;
    private const int BaseCornerRadius = 3;
    private const int BaseNameHeight = 14;
    private const int BaseNameOutline = 3;

    private ImageTexture _bitmap;
    private int _mapWidth;
    private int _mapHeight;
    private Vector2 _playerPos;
    private Direction _playerFacing;
    private bool _hasPlayer;
    private List<UiScaleLayout.GeomRecord> _geom;
    private Control _view;
    private Label _name;
    private StyleBoxFlat _frame;

    public void SetMap(int mapWidth, int mapHeight, ImageTexture bitmap, string mapName)
    {
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        _bitmap = bitmap;
        _hasPlayer = false;
        _name.Text = mapName;
        _view.QueueRedraw();
    }

    public void Invalidate() => _view.QueueRedraw();

    public void SetEnabled(bool enabled) => Visible = enabled;

    public void SetOpacity(float opacity) => Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(opacity, 0f, 1f));

    public void Relayout()
    {
        float factor = UiScaleApplier.Instance.Factor;
        UiScaleLayout.Apply(_geom, factor);
        // The view's scaled inset is the border width, so the frame always hugs the map.
        _frame.SetBorderWidthAll((int)_view.OffsetLeft);
        _frame.SetCornerRadiusAll(UiScaleApplier.Instance.ScaleSize(BaseCornerRadius));
        _name.AddThemeConstantOverride("outline_size", UiScaleApplier.Instance.ScaleSize(BaseNameOutline));
        QueueRedraw();
    }

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -BasePixels - 2 * BaseBorder - 8;
        OffsetTop = 28;
        OffsetRight = -8;
        OffsetBottom = 28 + BasePixels + 2 * BaseBorder;
        MouseFilter = MouseFilterEnum.Ignore;

        var panel = GetThemeStylebox("panel", "Panel") as StyleBoxFlat;
        _frame = new StyleBoxFlat
        {
            DrawCenter = false,
            BorderColor = panel?.BorderColor ?? new Color(0.584314f, 0.447059f, 0.235294f),
            ShadowColor = panel?.ShadowColor ?? new Color(0, 0, 0, 0.55f),
            ShadowSize = panel?.ShadowSize ?? 3,
            AntiAliasing = true,
        };

        _view = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
        _view.SetAnchorsPreset(LayoutPreset.FullRect);
        _view.OffsetLeft = BaseBorder;
        _view.OffsetTop = BaseBorder;
        _view.OffsetRight = -BaseBorder;
        _view.OffsetBottom = -BaseBorder;
        _view.Draw += DrawMap;
        AddChild(_view);

        _name = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _name.AddThemeColorOverride("font_outline_color", Colors.Black);
        _name.SetAnchorsPreset(LayoutPreset.BottomWide);
        _name.OffsetLeft = 0;
        _name.OffsetRight = 0;
        _name.OffsetTop = 2;
        _name.OffsetBottom = 2 + BaseNameHeight;
        AddChild(_name);

        Visible = GameManager.Instance?.CharacterSettings.GetOption<bool>(Options.Minimap, true) ?? true;
        SetOpacity(Mathf.Clamp(
            GameManager.Instance?.CharacterSettings.GetOption<float>(Options.MinimapOpacity, 1f) ?? 1f,
            0.2f, 1f));

        _geom = UiScaleLayout.Snapshot(this);
        UiScaleApplier.Instance.RegisterWindow(this);
        Relayout();
        TreeExited += () => UiScaleApplier.Instance.UnregisterWindow(this);
    }

    public override void _Process(double delta)
    {
        var player = GameManager.Instance?.CurrentMapManager?.LocalPlayer;
        if (player != null)
        {
            _playerPos = player.GlobalPosition;
            _playerFacing = player.Facing;
            _hasPlayer = true;
        }
        else if (_hasPlayer)
        {
            _hasPlayer = false;
        }
        if (_bitmap != null) _view.QueueRedraw();
    }

    public override void _Draw()
    {
        if (_bitmap == null) return;
        DrawStyleBox(_frame, new Rect2(Vector2.Zero, Size));
    }

    private void DrawMap()
    {
        if (_bitmap == null) return;

        float px = _view.Size.X / WindowTiles;
        // Fractional bitmap coordinates: bitmap pixel (x, y) covers world [x*32, (x+1)*32).
        float bx = _hasPlayer ? _playerPos.X / MapCoords.TileSize : 0;
        float by = _hasPlayer ? _playerPos.Y / MapCoords.TileSize : 0;
        float camX = Mathf.Clamp(bx - WindowTiles / 2f, 0, Mathf.Max(0, _mapWidth - WindowTiles));
        float camY = Mathf.Clamp(by - WindowTiles / 2f, 0, Mathf.Max(0, _mapHeight - WindowTiles));

        _view.DrawTextureRect(_bitmap, new Rect2(-camX * px, -camY * px, _mapWidth * px, _mapHeight * px), false, Colors.White);

        if (!_hasPlayer) return;
        var center = new Vector2((bx - camX) * px, (by - camY) * px);
        float r = px * 4f / 3f;
        // Direction is Up, Right, Down, Left: a quarter turn clockwise per step from Up.
        float angle = (int)_playerFacing * Mathf.Pi / 2f;
        _view.DrawColoredPolygon(new[]
        {
            center + new Vector2(0, -r).Rotated(angle),
            center + new Vector2(-r, r).Rotated(angle),
            center + new Vector2(r, r).Rotated(angle)
        }, Colors.White);

        var mm = GameManager.Instance?.CurrentMapManager;
        if (mm == null) return;
        float dr = px - 0.5f;
        foreach (var c in mm.Characters)
        {
            if (c.IsLocalPlayer || c.IsHiddenFromViewer) continue;
            float mx = (c.GlobalPosition.X / MapCoords.TileSize - camX) * px;
            float my = (c.GlobalPosition.Y / MapCoords.TileSize - camY) * px;
            if (mx < -dr || my < -dr || mx > _view.Size.X + dr || my > _view.Size.Y + dr) continue;
            _view.DrawCircle(new Vector2(mx, my), dr, MarkerColor(c));
        }
    }

    private static Color MarkerColor(Character.Character c)
    {
        if (GameManager.Instance != null && GameManager.Instance.IsInParty(c.LoginId)) return GameColors.Yellow;
        return c.CharacterType switch
        {
            CharacterType.Monster => GameColors.Red,
            CharacterType.Vendor or CharacterType.Banker or CharacterType.Quest => GameColors.Green,
            CharacterType.Pet => GameColors.Yellow,
            _ => GameColors.White,
        };
    }
}
