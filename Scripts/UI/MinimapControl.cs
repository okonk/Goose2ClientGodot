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

    public void Relayout() => UiScaleLayout.Apply(_geom, UiScaleApplier.Instance.Factor);

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        ClipContents = true;
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -BasePixels - 8;
        OffsetTop = 8;
        OffsetRight = -8;
        OffsetBottom = 8 + BasePixels;
        MouseFilter = MouseFilterEnum.Ignore;

        _geom = UiScaleLayout.Snapshot(this);
        UiScaleApplier.Instance.RegisterWindow(this);
        Relayout();
        TreeExited += () => UiScaleApplier.Instance.UnregisterWindow(this);
    }

    public override void _Process(double delta)
    {
        var player = GameManager.Instance?.CurrentMapManager?.LocalPlayer;
        if (player == null)
        {
            if (_hasPlayer)
            {
                _hasPlayer = false;
                QueueRedraw();
            }
            return;
        }
        if (_bitmap == null) return;
        var pos = player.GlobalPosition;
        if (!_hasPlayer || pos.DistanceSquaredTo(_playerPos) > 0.0001f)
        {
            _playerPos = pos;
            _hasPlayer = true;
            QueueRedraw();
        }
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
    }
}
