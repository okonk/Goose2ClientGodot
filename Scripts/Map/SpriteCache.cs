using System.Collections.Generic;
using Godot;

namespace Goose2Client.Map;

/// <summary>Runtime sprite lookup: (sheet, graphic) → AtlasTexture region of the sheet PNG.
/// Replaces Unity's ResourceManager.LoadSprite("{sheet}-{graphic}") / Helpers.GetSprite.
/// Sheets load lazily from res://Assets/Sprites/sheets/{sheet}.png; rects come from the manifest.</summary>
public sealed class SpriteCache
{
    private const string SheetsDir = "res://Assets/Sprites/sheets";
    private const string ManifestPath = "res://Assets/Sprites/manifest.json";

    private readonly SpriteManifest _manifest;
    private readonly Dictionary<int, Texture2D> _sheets = new();
    private readonly Dictionary<(int, int), AtlasTexture> _tiles = new();

    public SpriteCache() : this(SpriteManifest.Load(ProjectSettings.GlobalizePath(ManifestPath))) { }
    public SpriteCache(SpriteManifest manifest) => _manifest = manifest;

    /// <summary>The AtlasTexture for (sheet, graphic), or null when sheet==0, the manifest has no
    /// such rect, or the PNG is missing.</summary>
    public AtlasTexture Get(int sheet, int graphic)
    {
        if (sheet == 0) return null;
        var key = (sheet, graphic);
        if (_tiles.TryGetValue(key, out var cached)) return cached;

        if (!_manifest.TryGetRect(sheet, graphic, out var r)) return null;
        var tex = LoadSheet(sheet);
        if (tex == null) return null;

        var atlas = new AtlasTexture { Atlas = tex, Region = new Rect2(r.X, r.Y, r.W, r.H) };
        _tiles[key] = atlas;
        return atlas;
    }

    private Texture2D LoadSheet(int sheet)
    {
        if (_sheets.TryGetValue(sheet, out var t)) return t;
        var path = $"{SheetsDir}/{sheet}.png";
        var tex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        if (tex == null) GD.PushWarning($"SpriteCache: missing sheet PNG {path}");
        _sheets[sheet] = tex;
        return tex;
    }
}
