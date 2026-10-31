using System.Text;
using System.Text.Json;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;
using Goose2.AssetConverter.SpriteFrames;

namespace Goose2.AssetConverter.Manifest;

public static class AppearanceManifestBuilder
{
    // Down keeps the original unsuffixed variant names so the map editor can fall back to it.
    private static readonly (string Variant, string Clip)[] Variants =
    {
        ("noEquip", "idle-no-equip-down"),
        ("equip", "idle-equip-down"),
        ("noEquipUp", "idle-no-equip-up"),
        ("equipUp", "idle-equip-up"),
        ("noEquipRight", "idle-no-equip-right"),
        ("equipRight", "idle-equip-right"),
        ("noEquipLeft", "idle-no-equip-left"),
        ("equipLeft", "idle-equip-left"),
    };

    private static readonly AnimationType[] KindOrder =
    {
        AnimationType.Body, AnimationType.Hair, AnimationType.Eyes, AnimationType.Chest,
        AnimationType.Helm, AnimationType.Legs, AnimationType.Feet, AnimationType.Hand,
    };

    public static string Build(IEnumerable<CompiledSpriteFramesResource> resources)
    {
        var parts = new Dictionary<AnimationType, SortedDictionary<int, int[]?[]>>();
        foreach (AnimationType kind in KindOrder)
            parts[kind] = new SortedDictionary<int, int[]?[]>();

        foreach (var resource in resources)
        {
            var frames = Variants.Select(v => FirstFrame(resource, v.Clip)).ToArray();
            if (frames[0] is null && frames[1] is null)
                continue;

            var ids = parts[resource.Type];
            if (!ids.TryGetValue(resource.Id, out var merged))
            {
                ids[resource.Id] = frames;
                continue;
            }

            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] is not { } frame)
                    continue;
                if (merged[i] is not null && !merged[i]!.SequenceEqual(frame))
                    throw new InvalidOperationException($"Conflicting appearance entries for {resource.Type}-{resource.Id}");
                merged[i] = frame;
            }
        }

        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 2);
            writer.WritePropertyName("parts");
            writer.WriteStartObject();
            foreach (AnimationType kind in KindOrder)
            {
                writer.WritePropertyName(kind.ToString());
                writer.WriteStartObject();
                foreach (var (id, frames) in parts[kind])
                {
                    writer.WritePropertyName(id.ToString());
                    writer.WriteStartObject();
                    for (int i = 0; i < Variants.Length; i++)
                    {
                        if (frames[i] is { } frame)
                            WriteFrame(writer, Variants[i].Variant, frame);
                    }
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
