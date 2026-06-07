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
    private readonly MapLayer[] _layers = new MapLayer[5];   // immediate-draw bands; [2] is null (see _objectLayer)
    private ObjectLayer _objectLayer;   // layer 2 ("Objects 1") as per-object Y-sortable sprites
    private Node2D _objects;     // dropped-item container
    private Camera2D _camera;
    private readonly System.Collections.Generic.Dictionary<int, MapItem> _mapObjects = new();
    private int _myLoginId = -1;
    private readonly System.Collections.Generic.Dictionary<int, Character.Character> _characters = new();
    private Node2D _characterRoot;
    private Character.Character _localPlayer;
    private bool _listenersRegistered;

    /// <summary>Weapon speed in ms received from the server via WPS (0 = not yet set).</summary>
    public int WeaponSpeed { get; private set; }

    /// <summary>The login ID of the local player.</summary>
    public int MyLoginId => _myLoginId;

    /// <summary>Look up a character node by login ID.</summary>
    public Character.Character GetCharacter(int loginId) => _characters.TryGetValue(loginId, out var c) ? c : null;

    /// <summary>The local player's character node (if alive).</summary>
    public Character.Character LocalPlayer => GetCharacter(_myLoginId);

    public System.Collections.Generic.IEnumerable<Character.Character> Characters => _characters.Values;

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
            if (i == 2) continue;   // layer 2 is the Y-sorted ObjectLayer (built below), not a flat band
            var layer = new MapLayer { Name = $"Layer{i}" };
            layersRoot.AddChild(layer);
            layer.Setup(_map, i, _cache);
            _layers[i] = layer;
        }

        // Layer 2 ("Objects 1": trees, walls) shares the characters' z_index (15) and Y-sorts with them,
        // so the player passes in front of an object's base and behind its top. It must be a direct
        // Y-sort child of this (Y-sort) node to merge into the same sort as the Characters node; placing
        // it just before Characters makes a character win the tie when it shares an object's base tile.
        _objectLayer = new ObjectLayer { Name = "Objects1", ZIndex = 15, YSortEnabled = true };
        AddChild(_objectLayer);
        MoveChild(_objectLayer, _characterRoot.GetIndex());
        _objectLayer.Setup(_map, 2, _cache);

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
        pm.Listen<VitalsPercentagePacket>(OnVitals);
        pm.Listen<WeaponSpeedPacket>(OnWeaponSpeed);
        pm.Listen<BattleTextPacket>(OnBattleText);
        pm.Listen<ChatPacket>(OnChatBubble);
        pm.Listen<EmotePacket>(OnEmote);
        pm.Listen<SpellCharacterPacket>(OnSpellCharacter);
        pm.Listen<SpellTilePacket>(OnSpellTile);
        pm.Listen<CastPacket>(OnCast);
        _listenersRegistered = true;

        GameManager.Instance.CurrentMapManager = this;
        GameManager.Instance.EnsureHud();
    }

    public override void _ExitTree()
    {
        // Always clear the back-reference, even if listeners were never registered.
        if (GameManager.Instance != null && GameManager.Instance.CurrentMapManager == this)
            GameManager.Instance.CurrentMapManager = null;

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
        pm.Remove<VitalsPercentagePacket>(OnVitals);
        pm.Remove<WeaponSpeedPacket>(OnWeaponSpeed);
        pm.Remove<BattleTextPacket>(OnBattleText);
        pm.Remove<ChatPacket>(OnChatBubble);
        pm.Remove<EmotePacket>(OnEmote);
        pm.Remove<SpellCharacterPacket>(OnSpellCharacter);
        pm.Remove<SpellTilePacket>(OnSpellTile);
        pm.Remove<CastPacket>(OnCast);
    }

    /// <summary>Bounds + blocked + occupancy check (Unity IsValidMove).</summary>
    public bool IsValidMove(int x, int y)
    {
        if (_map == null || x < 0 || y < 0 || x >= _map.Width || y >= _map.Height) return false;
        if (_map[x, y].IsBlocked) return false;
        foreach (var c in _characters.Values)
            if (c.X == x && c.Y == y) return false;
        return true;
    }

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
        if (c == _localPlayer) GameManager.Instance.OnCharacterUpdated(c);
    }

    private void OnSetYourCharacter(object packetObj)
    {
        var p = (SetYourCharacterPacket)packetObj;
        _myLoginId = p.LoginId;
        if (_characters.TryGetValue(p.LoginId, out var c)) AttachLocalPlayer(c);
        if (c == _localPlayer) GameManager.Instance.OnCharacterUpdated(c);
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
        if (_characters.TryGetValue(p.LoginId, out var c))
        {
            c.SetAppearance(p);
            if (c == _localPlayer) GameManager.Instance.OnCharacterUpdated(c);
        }
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

    private void OnVitals(object packetObj)
    {
        var p = (VitalsPercentagePacket)packetObj;
        if (_characters.TryGetValue(p.LoginId, out var c)) c.SetVitals(p.HPPercentage, p.MPPercentage);
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

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GameManager.Instance.IsTargeting) return;   // spell targeting suppresses world clicks
        if (@event is not InputEventMouseButton mb || !mb.Pressed) return;
        if (mb.ButtonIndex != MouseButton.Left && mb.ButtonIndex != MouseButton.Right) return;

        var mouseWorld = GetGlobalMousePosition();
        Character.Character hit = null;
        foreach (var child in _characterRoot.GetChildren())
        {
            if (child is not Character.Character c || !GodotObject.IsInstanceValid(c)) continue;
            if (c.ContainsPoint(mouseWorld)) hit = c;   // later children draw on top → last match is topmost
        }
        int tx, ty;
        if (hit != null) { tx = hit.X; ty = hit.Y; }
        else { (tx, ty) = MapCoords.WorldToTile(mouseWorld); }
        if (mb.ButtonIndex == MouseButton.Left)
            GameManager.Instance.NetworkClient.LeftClick(tx, ty);
        else
            GameManager.Instance.NetworkClient.RightClick(tx, ty);
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

    private void OnWeaponSpeed(object packetObj) => WeaponSpeed = ((WeaponSpeedPacket)packetObj).Speed;

    private void OnBattleText(object packetObj)
    {
        var p = (BattleTextPacket)packetObj;
        GetCharacter(p.LoginId)?.AddBattleText(p.BattleTextType, p.Text);
    }

    private void OnChatBubble(object packetObj)
    {
        var p = (ChatPacket)packetObj;
        GetCharacter(p.LoginId)?.ShowChatBubble(p.Message);
    }

    private void OnEmote(object packetObj)
    {
        var p = (EmotePacket)packetObj;
        GetCharacter(p.LoginId)?.ShowEmote(p.AnimationId);
    }

    private void OnSpellCharacter(object packetObj)
    {
        var p = (SpellCharacterPacket)packetObj;
        GetCharacter(p.LoginId)?.ShowSpell(p.AnimationId);
    }

    private void OnSpellTile(object packetObj)
    {
        var p = (SpellTilePacket)packetObj;
        var s = new Goose2Client.Overlays.SpellAnimation { Name = $"SpellTile({p.AnimationId})", ZIndex = 20 };
        _objects.AddChild(s);
        if (!s.Setup(p.AnimationId)) { s.QueueFree(); return; }
        s.Position = MapCoords.TileCenter(p.TileX, p.TileY);   // centered on the tile cell
    }

    private void OnCast(object packetObj)
    {
        var p = (CastPacket)packetObj;
        GetCharacter(p.LoginId)?.Cast();
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
            if (layer == 2) _objectLayer.RefreshCell(p.X, p.Y);        // Y-sorted layer: rebuild the cell
            else _layers[layer].QueueRedraw();                         // flat band: repaint the layer
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
        item.Item = ItemStats.FromPacket(p);
        _mapObjects[ItemKey(p.TileX, p.TileY)] = item;
    }

    private void OnEraseObject(object packetObj)
    {
        var p = (EraseObjectPacket)packetObj;
        if (_mapObjects.Remove(ItemKey(p.TileX, p.TileY), out var item))
            item.QueueFree();
    }
}
