using System.Collections.Generic;
using Godot;
using Goose2Client.Map;

namespace Goose2Client.UI;

public partial class MinimapControl : Control, IScalableWindow
{
    public const int WindowTiles = 64;
    public const int BasePixels = 128;

    private ImageTexture _bitmap;
    private int _mapWidth;
    private int _mapHeight;
    private Vector2 _playerPos;
    private bool _hasPlayer;
    private List<UiScaleLayout.GeomRecord> _geom;

    public void SetMap(int mapWidth, int mapHeight, ImageTexture bitmap)
    {
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        _bitmap = bitmap;
        _hasPlayer = false;
        QueueRedraw();
    }

    public void Invalidate() => QueueRedraw();

    public void SetEnabled(bool enabled) => Visible = enabled;

    public void Relayout() => UiScaleLayout.Apply(_geom, UiScaleApplier.Instance.Factor);

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        ClipContents = true;
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -BasePixels - 8;
        OffsetTop = 28;
        OffsetRight = -8;
        OffsetBottom = 28 + BasePixels;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = GameManager.Instance?.CharacterSettings.GetOption<bool>(Options.Minimap, true) ?? true;

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
            _hasPlayer = true;
        }
        else if (_hasPlayer)
        {
            _hasPlayer = false;
        }
        if (_bitmap != null) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_bitmap == null) return;

        float px = Size.X / WindowTiles;
        // Fractional bitmap coordinates: bitmap pixel (x, y) covers world [x*32, (x+1)*32).
        float bx = _hasPlayer ? _playerPos.X / MapCoords.TileSize : 0;
        float by = _hasPlayer ? _playerPos.Y / MapCoords.TileSize : 0;
        float camX = Mathf.Clamp(bx - WindowTiles / 2f, 0, Mathf.Max(0, _mapWidth - WindowTiles));
        float camY = Mathf.Clamp(by - WindowTiles / 2f, 0, Mathf.Max(0, _mapHeight - WindowTiles));

        DrawTextureRect(_bitmap, new Rect2(-camX * px, -camY * px, _mapWidth * px, _mapHeight * px), false, Colors.White);

        if (!_hasPlayer) return;
        float ax = (bx - camX) * px;
        float ay = (by - camY) * px;
        float r = px * 4f / 3f;
        DrawColoredPolygon(new[]
        {
            new Vector2(ax, ay - r),
            new Vector2(ax - r, ay + r),
            new Vector2(ax + r, ay + r)
        }, Colors.White);

        var mm = GameManager.Instance?.CurrentMapManager;
        if (mm == null) return;
        float dr = px - 0.5f;
        foreach (var c in mm.Characters)
        {
            if (c.IsLocalPlayer || c.IsHiddenFromViewer) continue;
            float mx = (c.GlobalPosition.X / MapCoords.TileSize - camX) * px;
            float my = (c.GlobalPosition.Y / MapCoords.TileSize - camY) * px;
            if (mx < -dr || my < -dr || mx > Size.X + dr || my > Size.Y + dr) continue;
            DrawCircle(new Vector2(mx, my), dr, MarkerColor(c));
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
