using Godot;
using Goose2Client.Map;
using Goose2Client.Network.Packets;
using Goose2Client.UI;
using MapEditor.Core;

namespace Goose2Client;

/// <summary>World root for the active map (port of Unity MapManager, map/tile/item subset;
/// character handling is Step 6). Builds flat TileMapLayer bands + a Y-sorted ObjectLayer,
/// runs the Camera2D, and handles TileUpdate / MapObject / EraseObject / SetYourPosition.</summary>
public partial class MapManager : Node2D
{
    private MapDocument _map;
    private SpriteCache _cache;
    private MapTileCatalog _tileCatalog;
    private readonly MapLayer[] _layers = new MapLayer[5];   // TileMapLayer bands; [2] is null (see _objectLayer)
    private ObjectLayer _objectLayer;   // layer 2 ("Objects 1") as per-object Y-sortable sprites
    private Node2D _objects;     // dropped-item container
    private Camera2D _camera;
    private readonly System.Collections.Generic.Dictionary<int, MapItem> _mapObjects = new();
    private int _myLoginId = -1;
    private readonly System.Collections.Generic.Dictionary<int, Character.Character> _characters = new();
    private Image _minimapImage;
    private ImageTexture _minimapTexture;
    private readonly System.Collections.Generic.Dictionary<(int, int), Color?> _tileColorMemo = new();
    private Node2D _characterRoot;
    private Character.Character _localPlayer;
    private bool _listenersRegistered;

    // A fast double-click sends two LCs before the first MKW lands; the server treats the
    // second as a fresh open and strands the first window. Drop same-tile repeats.
    private const int LcDedupMs = 500;
    private Vector2I _lastLcTile = new(-1, -1);
    private ulong _lastLcMsec;

    /// <summary>Weapon speed in ms received from the server via WPS (0 = not yet set).</summary>
    public int WeaponSpeed { get; private set; }

    /// <summary>The login ID of the local player.</summary>
    public int MyLoginId => _myLoginId;

    /// <summary>Look up a character node by login ID.</summary>
    public Character.Character GetCharacter(int loginId) => _characters.TryGetValue(loginId, out var c) ? c : null;

    /// <summary>The local player's character node (if alive).</summary>
    public Character.Character LocalPlayer => GetCharacter(_myLoginId);

    public System.Collections.Generic.IEnumerable<Character.Character> Characters => _characters.Values;

    /// <summary>Camera follow must run AFTER every Character has moved this frame. MapManager
    /// sits on "World", the PARENT of "Characters", and equal-priority nodes process in tree
    /// order (parents first), so at the default 0 the camera reads the previous frame's player
    /// position and trails it by one frame. Harmless while everything was fractional; once both
    /// camera and character snap to whole pixels it shows up as the player visibly bouncing
    /// between a 2px and 3px offset from centre as the step pattern alternates.
    /// Below WorldTextBridge's 100 so world text still projects after the camera settles
    /// (tools/tests/text_bridge_order.gd pins the priority contract).</summary>
    private const int CameraFollowPriority = 50;

    public override void _EnterTree() => ProcessPriority = CameraFollowPriority;   // before the first _Process

    public override void _Ready()
    {
        _map = GameManager.Instance.CurrentMap;
        _cache = new SpriteCache();
        _tileCatalog = new MapTileCatalog(_cache);
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
            layer.Setup(_map, i, _tileCatalog);
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
        pm.Listen<AdminModeActivatePacket>(OnAdminModeActivate);
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
        pm.Listen<SeeInvisiblePacket>(OnSeeInvisible);
        _listenersRegistered = true;

        GameManager.Instance.CurrentMapManager = this;
        GameManager.Instance.EnsureHud();

        _minimapImage = MinimapBitmapBuilder.Build(_map, TileColor);
        _minimapTexture = ImageTexture.CreateFromImage(_minimapImage);
        GameManager.Instance.Hud?.Minimap?.SetMap(_map.Width, _map.Height, _minimapTexture);
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
        pm.Remove<AdminModeActivatePacket>(OnAdminModeActivate);
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
        pm.Remove<SeeInvisiblePacket>(OnSeeInvisible);
    }

    /// <summary>Bounds + blocked + occupancy check (Unity IsValidMove).</summary>
    public bool IsValidMove(int x, int y)
    {
        if (_map == null || x < 0 || y < 0 || x >= _map.Width || y >= _map.Height) return false;
        // GMs (MKC IsGM / AMA enabled) walk through blocked tiles; bounds and occupancy still apply.
        if (_map[x, y].IsBlocked && LocalPlayer?.IsGM != true) return false;
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

    private void OnAdminModeActivate(object packetObj)
    {
        var p = (AdminModeActivatePacket)packetObj;
        GetCharacter(p.LoginId)?.SetGm(p.Enabled != 0);
    }

    private void OnSeeInvisible(object packetObj)
    {
        // Per-map listener is safe only because the server sends SINVS after map load — a pre-map SINVS would be dropped.
        GameManager.Instance.CanSeeInvisible = ((SeeInvisiblePacket)packetObj).CanSee;
        foreach (var c in _characters.Values) c.ApplyInvisibility();
    }

    private void OnSetYourCharacter(object packetObj)
    {
        var p = (SetYourCharacterPacket)packetObj;
        _myLoginId = p.LoginId;
        if (!_characters.TryGetValue(p.LoginId, out var c))
            return; // Unity returns unknown / MKC attaches later
        AttachLocalPlayer(c);
        GameManager.Instance.OnCharacterUpdated(c);
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
            GameManager.Instance?.SpellTargetManager?.OnCharacterErased(c);
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
        c.ApplyInvisibility();
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
            UpdateRoofVisibility(_localPlayer.X, _localPlayer.Y);   // roof toggle keyed on tile
            // Smooth-follow the lerped position, but on whole pixels: the world renders 1:1 into
            // the sub-viewport and is blitted at an integer scale, so a fractional camera makes
            // tile and sprite edges rasterize inconsistently (1px seams, shimmer). Rounded here,
            // WorldViewportScale.CameraParityOffset makes the whole canvas transform integral.
            _camera.GlobalPosition = _localPlayer.Position.Round();
            // Camera2D pushes its transform to the viewport from its own internal process at
            // priority 0 — i.e. BEFORE this write — and the transform-changed notification that
            // would refresh it is deferred to the end of the frame. Rendering picks that up, but
            // anything reading GetCanvasTransform() during processing (WorldTextBridge, at 100)
            // would project this frame's character position through last frame's camera and jitter
            // by the step size. Push it now (tools/tests/camera_scroll_order.gd).
            _camera.ForceUpdateScroll();
        }
    }

    /// <summary>World-space click. `worldPos` is in map pixels (see WorldViewport.WindowToWorld).</summary>
    public void HandleWorldClick(MouseButton button, Vector2 worldPos)
    {
        if (GameManager.Instance.IsTargeting) return;   // spell targeting suppresses world clicks
        Character.Character hit = null;
        foreach (var child in _characterRoot.GetChildren())
        {
            if (child is not Character.Character c || !GodotObject.IsInstanceValid(c)) continue;
            if (c.ContainsPoint(worldPos)) hit = c;   // later children draw on top → last match is topmost
        }
        int tx, ty;
        if (hit != null) { tx = hit.X; ty = hit.Y; }
        else { (tx, ty) = MapCoords.WorldToTile(worldPos); }
        if (button == MouseButton.Left)
        {
            // The server drops the NPC's prior quest window on a re-click without telling the
            // client, so a second LC would leave a dead window behind.
            var questWindows = GameManager.Instance.Hud?.QuestWindows;
            if (hit != null && questWindows != null && questWindows.HasWindowForNpc(hit.LoginId))
                return;
            ulong now = Time.GetTicksMsec();
            var tile = new Vector2I(tx, ty);
            if (tile == _lastLcTile && now - _lastLcMsec < LcDedupMs)
                return;
            _lastLcTile = tile;
            _lastLcMsec = now;
            GameManager.Instance.NetworkClient.LeftClick(tx, ty);
        }
        else
            GameManager.Instance.NetworkClient.RightClick(tx, ty);
    }

    private void CenterCameraOn(int x, int y)
    {
        _camera.GlobalPosition = MapCoords.TileCenter(x, y);
        _camera.ForceUpdateScroll();   // same-frame canvas transform for world-text projection (see _Process)
        UpdateRoofVisibility(x, y);
    }

    /// <summary>Roof layer hides when the player stands under it (Unity roofLayer.SetActive(!IsRoof)).</summary>
    private void UpdateRoofVisibility(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _map.Width || y >= _map.Height) return;
        _layers[4].Visible = !_map[x, y].IsRoof;
    }

    /// <summary>True when the visible roof band covers tile (x,y). Overhead text lives on a
    /// CanvasLayer above the world canvas, so "roof above names" is done by hiding, not z-order.</summary>
    public bool IsRoofOccluding(int x, int y)
    {
        if (_map == null || _layers[4] == null) return false;
        if (x < 0 || y < 0 || x >= _map.Width || y >= _map.Height) return false;
        return _layers[4].Visible && _map[x, y].IsRoof;
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
        // Absolute z 20: above characters/Objects1 (15), below Objects2 (30) — Unity puts spell
        // effects on sorting layer "Objects 1" order 100. Relative z would land at 14+20=34.
        var s = new Goose2Client.Overlays.SpellAnimation { Name = $"SpellTile({p.AnimationId})", ZIndex = 20, ZAsRelative = false };
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

        MapTileUpdate.Apply(_map, p.X, p.Y, p.Flags, p.Tiles, layer =>
        {
            if (layer == 2) _objectLayer.RefreshCell(p.X, p.Y);        // Y-sorted layer: rebuild the cell
            else _layers[layer].RefreshCell(p.X, p.Y);                 // TileMapLayer: update one cell
        });

        _minimapImage.SetPixelv(new Vector2I(p.X, p.Y), MinimapColors.PickColor(_map[p.X, p.Y], TileColor) ?? Colors.Black);
        _minimapTexture.Update(_minimapImage);
        GameManager.Instance.Hud?.Minimap?.Invalidate();
    }

    private Color? TileColor(int sheet, int graphic)
    {
        var key = (sheet, graphic);
        if (_tileColorMemo.TryGetValue(key, out var cached)) return cached;

        Color? color = null;
        var atlas = _cache.Get(sheet, graphic);
        if (atlas != null)
        {
            var img = atlas.Atlas.GetImage();
            var r = atlas.Region;
            int x0 = (int)r.Position.X, x1 = (int)(r.Position.X + r.Size.X);
            int y0 = (int)r.Position.Y, y1 = (int)(r.Position.Y + r.Size.Y);
            double cr = 0, cg = 0, cb = 0;
            int count = 0;
            for (int py = y0; py < y1; py++)
                for (int px = x0; px < x1; px++)
                {
                    var c = img.GetPixelv(new Vector2I(px, py));
                    if (c.A > 0)
                    {
                        cr += c.R; cg += c.G; cb += c.B;
                        count++;
                    }
                }
            if (count > 0)
                color = new Color((float)(cr / count), (float)(cg / count), (float)(cb / count), 1f);
        }
        _tileColorMemo[key] = color;
        return color;
    }

    internal static int ItemKey(MapDocument map, int x, int y) => y * map.Width + x;

    private int ItemKey(int x, int y) => ItemKey(_map, x, y);

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
