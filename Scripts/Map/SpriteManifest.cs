using System.Collections.Generic;
using System.Text.Json;

namespace Goose2Client.Map;

public readonly record struct FrameRect(int X, int Y, int W, int H);

/// <summary>In-memory view of Assets/Sprites/manifest.json: (sheet, graphic) → pixel rect.
/// Parser only — turning a rect into a Godot AtlasTexture is SpriteCache's job.</summary>
public sealed class SpriteManifest
{
    private readonly Dictionary<int, Dictionary<int, FrameRect>> _sheets;

    private SpriteManifest(Dictionary<int, Dictionary<int, FrameRect>> sheets) => _sheets = sheets;

    public bool TryGetRect(int sheet, int graphic, out FrameRect rect)
    {
        rect = default;
        return _sheets.TryGetValue(sheet, out var g) && g.TryGetValue(graphic, out rect);
    }

    public static SpriteManifest Parse(string json)
    {
        var sheets = new Dictionary<int, Dictionary<int, FrameRect>>();
        using var doc = JsonDocument.Parse(json);
        foreach (var sheet in doc.RootElement.GetProperty("sheets").EnumerateObject())
        {
            var frames = new Dictionary<int, FrameRect>();
            foreach (var frame in sheet.Value.EnumerateObject())
            {
                var a = frame.Value;
                frames[int.Parse(frame.Name)] =
                    new FrameRect(a[0].GetInt32(), a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt32());
            }
            sheets[int.Parse(sheet.Name)] = frames;
        }
        return new SpriteManifest(sheets);
    }

    public static SpriteManifest Load(string path) => Parse(System.IO.File.ReadAllText(path));
}
