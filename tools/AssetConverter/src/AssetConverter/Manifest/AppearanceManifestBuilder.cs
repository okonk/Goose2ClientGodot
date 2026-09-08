using System.Text;
using System.Text.Json;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;
using Goose2.AssetConverter.SpriteFrames;

namespace Goose2.AssetConverter.Manifest;

public static class AppearanceManifestBuilder
{
    private const string NoEquipClip = "idle-no-equip-down";
    private const string EquipClip = "idle-equip-down";

    private static readonly AnimationType[] KindOrder =
    {
        AnimationType.Body, AnimationType.Hair, AnimationType.Eyes, AnimationType.Chest,
        AnimationType.Helm, AnimationType.Legs, AnimationType.Feet, AnimationType.Hand,
    };

    public static string Build(IEnumerable<CompiledSpriteFramesResource> resources)
    {
        var parts = new Dictionary<AnimationType, SortedDictionary<int, (int[]? NoEquip, int[]? Equip)>>();
        foreach (AnimationType kind in KindOrder)
            parts[kind] = new SortedDictionary<int, (int[]? NoEquip, int[]? Equip)>();

        foreach (var resource in resources)
        {
            int[]? noEquip = FirstFrame(resource, NoEquipClip);
            int[]? equip = FirstFrame(resource, EquipClip);
            if (noEquip is null && equip is null)
                continue;

            var ids = parts[resource.Type];
            if (!ids.TryGetValue(resource.Id, out var existing))
            {
                ids[resource.Id] = (noEquip, equip);
                continue;
            }

            var merged = existing;
            if (noEquip is not null)
            {
                if (merged.NoEquip is not null && !merged.NoEquip.SequenceEqual(noEquip))
                    throw new InvalidOperationException($"Conflicting appearance entries for {resource.Type}-{resource.Id}");
                merged.NoEquip = noEquip;
            }
            if (equip is not null)
            {
                if (merged.Equip is not null && !merged.Equip.SequenceEqual(equip))
                    throw new InvalidOperationException($"Conflicting appearance entries for {resource.Type}-{resource.Id}");
                merged.Equip = equip;
            }
            ids[resource.Id] = merged;
        }

        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WritePropertyName("parts");
            writer.WriteStartObject();
            foreach (AnimationType kind in KindOrder)
            {
                writer.WritePropertyName(kind.ToString());
                writer.WriteStartObject();
                foreach (var (id, entry) in parts[kind])
                {
                    writer.WritePropertyName(id.ToString());
                    writer.WriteStartObject();
                    if (entry.NoEquip is not null)
                        WriteFrame(writer, "noEquip", entry.NoEquip);
                    if (entry.Equip is not null)
                        WriteFrame(writer, "equip", entry.Equip);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static int[]? FirstFrame(CompiledSpriteFramesResource resource, string clipName)
    {
        foreach (var spec in resource.Animations)
        {
            if (spec.Name != clipName || spec.Frames.Count == 0)
                continue;
            var frame = spec.Frames[0];
            // The sprite manifest keys Aspereta graphics as GraphicBase + frame index,
            // not the raw ADF index, so the reference must match that space.
            int graphic = frame.SheetNumber >= AsperetaSheets.SheetBase
                ? AsperetaSheets.GraphicBase + frame.Frame.Index
                : frame.Frame.Index;
            return new[] { frame.SheetNumber, graphic };
        }
        return null;
    }

    private static void WriteFrame(Utf8JsonWriter writer, string name, int[] frame)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        writer.WriteNumberValue(frame[0]);
        writer.WriteNumberValue(frame[1]);
        writer.WriteEndArray();
    }
}
