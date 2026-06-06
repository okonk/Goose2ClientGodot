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
    private readonly System.Collections.Generic.Dictionary<int, MapItem> _mapObjects = new();
    private int _myLoginId = -1;
    private readonly System.Collections.Generic.Dictionary<int, Character.Character> _characters = new();
    private Node2D _characterRoot;
    private Character.Character _localPlayer;
    private bool _listenersRegistered;

    public override void _Ready()
    {
        _map = GameManager.Instance.CurrentMap;
        _cache = new SpriteCache();
        _objects = GetNode<Node2D>("Objects");
        _characterRoot = GetNode<Node2D>("Characters");
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
        pm.Listen<MakeCharacterPacket>(OnMakeCharacter);
        pm.Listen<SetYourCharacterPacket>(OnSetYourCharacter);
        pm.Listen<MoveCharacterPacket>(OnMoveCharacter);
        pm.Listen<ChangeHeadingPacket>(OnChangeHeading);
        pm.Listen<UpdateCharacterPacket>(OnUpdateCharacter);
        pm.Listen<EraseCharacterPacket>(OnEraseCharacter);
        pm.Listen<AttackPacket>(OnAttack);
        _listenersRegistered = true;
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        var pm = GameManager.Instance.PacketManager;
        pm.Remove<TileUpdatePacket>(OnTileUpdate);
        pm.Remove<MapObjectPacket>(OnMapObject);
        pm.Remove<EraseObjectPacket>(OnEraseObject);
        pm.Remove<SetYourPositionPacket>(OnSetYourPosition);
        pm.Remove<MakeCharacterPacket>(OnMakeCharacter);
        pm.Remove<SetYourCharacterPacket>(OnSetYourCharacter);
        pm.Remove<MoveCharacterPacket>(OnMoveCharacter);
        pm.Remove<ChangeHeadingPacket>(OnChangeHeading);
        pm.Remove<UpdateCharacterPacket>(OnUpdateCharacter);
        pm.Remove<EraseCharacterPacket>(OnEraseCharacter);
        pm.Remove<AttackPacket>(OnAttack);
    }

    /// <summary>Bounds + blocked check (Unity IsValidMove, map-only part; occupancy is Step 6).</summary>
    public bool IsValidMove(int x, int y)
        => _map != null && x >= 0 && y >= 0 && x < _map.Width && y < _map.Height && !_map[x, y].IsBlocked;

    private void OnMakeCharacter(object packetObj)
    {
        var p = (MakeCharacterPacket)packetObj;
        if (_characters.Remove(p.LoginId, out var existing))
            existing.QueueFree();

        var c = new Character.Character { Name = $"Char_{p.LoginId}" };
        _characterRoot.AddChild(c);
        c.SetAppearance(p);
        _characters[p.LoginId] = c;

        if (p.LoginId == _myLoginId) AttachLocalPlayer(c);
    }

    private void OnSetYourCharacter(object packetObj)
    {
        var p = (SetYourCharacterPacket)packetObj;
        _myLoginId = p.LoginId;
        if (_characters.TryGetValue(p.LoginId, out var c)) AttachLocalPlayer(c);
    }

    private void OnMoveCharacter(object packetObj)
    {
        var p = (MoveCharacterPacket)packetObj;
        if (_characters.TryGetValue(p.LoginId, out var c)) c.MoveTo(p.MapX, p.MapY);
    }

    private void OnChangeHeading(object packetObj)
    {
        var p = (ChangeHeadingPacket)packetObj;
        if (_characters.TryGetValue(p.LoginId, out var c)) c.SetFacing(p.Direction);
    }

    private void OnUpdateCharacter(object packetObj)
    {
        var p = (UpdateCharacterPacket)packetObj;
        if (_characters.TryGetValue(p.LoginId, out var c)) c.SetAppearance(p);
    }

    private void OnEraseCharacter(object packetObj)
    {
        var p = (EraseCharacterPacket)packetObj;
        if (_characters.Remove(p.LoginId, out var c))
        {
            if (c == _localPlayer) _localPlayer = null;
            c.QueueFree();
        }
    }

    private void OnAttack(object packetObj)
    {
        var p = (AttackPacket)packetObj;
        if (_characters.TryGetValue(p.LoginId, out var c)) c.TriggerAttack();
    }

    private void AttachLocalPlayer(Character.Character c)
    {
        _localPlayer = c;
        c.IsLocalPlayer = true;
        CenterCameraOn(c.X, c.Y);
    }

    private void OnSetYourPosition(object packetObj)
    {
        var p = (SetYourPositionPacket)packetObj;
        if (_localPlayer != null && GodotObject.IsInstanceValid(_localPlayer))
            _localPlayer.TeleportTo(p.MapX, p.MapY);
        CenterCameraOn(p.MapX, p.MapY);
    }

    public override void _Process(double delta)
    {
        if (_localPlayer != null && GodotObject.IsInstanceValid(_localPlayer))
        {
            CenterCameraOn(_localPlayer.X, _localPlayer.Y);   // roof toggle keyed on tile
            _camera.GlobalPosition = _localPlayer.Position;   // smooth-follow the lerped position
        }
    }

    private void CenterCameraOn(int x, int y)
    {
        _camera.GlobalPosition = MapCoords.TileCenter(x, y);
        UpdateRoofVisibility(x, y);
    }

    /// <summary>Roof layer hides when the player stands under it (Unity roofLayer.SetActive(!IsRoof)).</summary>
    private void UpdateRoofVisibility(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _map.Width || y >= _map.Height) return;
        _layers[4].Visible = !_map[x, y].IsRoof;
    }

    private void OnTileUpdate(object packetObj)
    {
        var p = (TileUpdatePacket)packetObj;
        if (p.X < 0 || p.Y < 0 || p.X >= _map.Width || p.Y >= _map.Height) return;

        var tile = _map[p.X, p.Y];
        tile.Flags = p.Flags;

        for (int layer = 0; layer < 5; layer++)
        {
            int graphic = p.Tiles[layer * 2];
            int sheet   = p.Tiles[layer * 2 + 1];
            var l = tile.Layers[layer];
            if (l.Graphic == graphic && l.Sheet == sheet) continue;   // unchanged

            l.Graphic = sheet == 0 ? 0 : graphic;                      // sheet 0 ⇒ empty cell
            l.Sheet   = sheet;
            _layers[layer].QueueRedraw();                              // repaint that layer
        }
    }

    private int ItemKey(int x, int y) => y * _map.Height + x;

    private void OnMapObject(object packetObj)
    {
        var p = (MapObjectPacket)packetObj;
        var tex = _cache.Get(p.GraphicFile, p.GraphicId);   // sheet=GraphicFile, graphic=GraphicId
        if (tex == null) return;

        if (_mapObjects.TryGetValue(ItemKey(p.TileX, p.TileY), out var existing))
            existing.QueueFree();

        var item = new MapItem { Name = $"{p.Name} ({p.GraphicId})" };
        _objects.AddChild(item);
        item.Setup(tex, p.TileX, p.TileY,
            new Color(p.GraphicR / 255f, p.GraphicG / 255f, p.GraphicB / 255f, p.GraphicA / 255f));
        _mapObjects[ItemKey(p.TileX, p.TileY)] = item;
    }

    private void OnEraseObject(object packetObj)
    {
        var p = (EraseObjectPacket)packetObj;
        if (_mapObjects.Remove(ItemKey(p.TileX, p.TileY), out var item))
            item.QueueFree();
    }
}
