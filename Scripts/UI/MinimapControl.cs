using System.Collections.Generic;
using Godot;
using Goose2Client.Map;

namespace Goose2Client.UI;

public partial class MinimapControl : Control, IScalableWindow
{
    public const int WindowTiles = 64;
    public const int BasePixels = 192;

    private ImageTexture _bitmap;
    private int _mapWidth;
    private int _mapHeight;
    private int _playerTileX = -1;
    private int _playerTileY = -1;
    private List<UiScaleLayout.GeomRecord> _geom;

    public void SetMap(int mapWidth, int mapHeight, ImageTexture bitmap)
    {
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        _bitmap = bitmap;
        _playerTileX = -1;
        _playerTileY = -1;
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
            if (_playerTileX != -1 || _playerTileY != -1)
            {
                _playerTileX = -1;
                _playerTileY = -1;
                QueueRedraw();
            }
            return;
        }
        if (_bitmap == null) return;
        var (tx, ty) = MapCoords.WorldToTile(player.GlobalPosition);
        if (tx != _playerTileX || ty != _playerTileY)
        {
            _playerTileX = tx;
            _playerTileY = ty;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_bitmap == null) return;

        int anchorX = _playerTileX >= 0 ? _playerTileX : 0;
        int anchorY = _playerTileY >= 0 ? _playerTileY : 0;
        int winX = Mathf.Clamp(anchorX - WindowTiles / 2, 0, Mathf.Max(0, _mapWidth - WindowTiles));
        int winY = Mathf.Clamp(anchorY - WindowTiles / 2, 0, Mathf.Max(0, _mapHeight - WindowTiles));

        float px = Size.X / WindowTiles;
        DrawTextureRect(_bitmap, new Rect2(-winX * px, -winY * px, _mapWidth * px, _mapHeight * px), false, Colors.White);

        if (_playerTileX < 0 || _playerTileY < 0 || _playerTileX >= _mapWidth || _playerTileY >= _mapHeight) return;
        float ax = (_playerTileX - winX) * px + px / 2;
        float ay = (_playerTileY - winY) * px + px / 2;
        float r = px * 4f / 3f;
        DrawColoredPolygon(new[]
        {
            new Vector2(ax, ay - r),
            new Vector2(ax - r, ay + r),
            new Vector2(ax + r, ay + r)
        }, Colors.White);
    }
}
