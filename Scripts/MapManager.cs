using Godot;
using Goose2Client.Map;
using Goose2Client.Network.Packets;

namespace Goose2Client;

/// <summary>World root for the active map (port of Unity MapManager, map/tile/item subset;
/// character handling is Step 6). Builds the 5 MapLayer nodes, runs the Camera2D, and handles
/// TileUpdate / MapObject / EraseObject / SetYourPosition.</summary>
public partial class MapManager : Node2D
{
    private MapFile _map;
    private SpriteCache _cache;
    private readonly MapLayer[] _layers = new MapLayer[5];
    private Node2D _objects;     // dropped-item container
    private Camera2D _camera;

    public override void _Ready()
    {
        _map = GameManager.Instance.CurrentMap;
        _cache = new SpriteCache();
        _objects = GetNode<Node2D>("Objects");
        _camera = GetNode<Camera2D>("Camera2D");

        if (_map == null) { GD.PushError("MapManager: CurrentMap is null"); return; }

        var layersRoot = GetNode<Node2D>("Layers");
        for (int i = 0; i < 5; i++)
        {
            var layer = new MapLayer { Name = $"Layer{i}" };
            layersRoot.AddChild(layer);
            layer.Setup(_map, i, _cache);
            _layers[i] = layer;
        }

        var pm = GameManager.Instance.PacketManager;
        pm.Listen<TileUpdatePacket>(OnTileUpdate);
        pm.Listen<MapObjectPacket>(OnMapObject);
        pm.Listen<EraseObjectPacket>(OnEraseObject);
        pm.Listen<SetYourPositionPacket>(OnSetYourPosition);
    }

    public override void _ExitTree()
    {
        var pm = GameManager.Instance.PacketManager;
        pm.Remove<TileUpdatePacket>(OnTileUpdate);
        pm.Remove<MapObjectPacket>(OnMapObject);
        pm.Remove<EraseObjectPacket>(OnEraseObject);
        pm.Remove<SetYourPositionPacket>(OnSetYourPosition);
    }

    /// <summary>Bounds + blocked check (Unity IsValidMove, map-only part; occupancy is Step 6).</summary>
    public bool IsValidMove(int x, int y)
        => x >= 0 && y >= 0 && x < _map.Width && y < _map.Height && !_map[x, y].IsBlocked;

    private void OnSetYourPosition(object packetObj)
    {
        var p = (SetYourPositionPacket)packetObj;
        _camera.GlobalPosition = MapCoords.TileCenter(p.MapX, p.MapY);
        UpdateRoofVisibility(p.MapX, p.MapY);
    }

    /// <summary>Roof layer hides when the player stands under it (Unity roofLayer.SetActive(!IsRoof)).</summary>
    private void UpdateRoofVisibility(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _map.Width || y >= _map.Height) return;
        _layers[4].Visible = !_map[x, y].IsRoof;
    }

    private void OnTileUpdate(object packetObj) { /* Task 9 */ }
    private void OnMapObject(object packetObj) { /* Task 8 */ }
    private void OnEraseObject(object packetObj) { /* Task 8 */ }
}
