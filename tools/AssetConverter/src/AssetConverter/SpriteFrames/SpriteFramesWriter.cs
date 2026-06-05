using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.SpriteFrames;

public static class SpriteFramesWriter
{
    /// <summary>Builds a Godot 4 SpriteFrames .tres putting all of the adf's frames into a
    /// single looping animation "all". Region coords are top-down (Godot AtlasTexture origin),
    /// matching Frame.X/Y directly.</summary>
    public static string Build(AdfFile adf, string texturePath, float speed = 8f)
    {
        var sb = new StringBuilder();
        int n = adf.Frames.Count;

        sb.AppendLine("[gd_resource type=\"SpriteFrames\" format=3]");
        sb.AppendLine();
        sb.AppendLine($"[ext_resource type=\"Texture2D\" path=\"{texturePath}\" id=\"1\"]");
        sb.AppendLine();

        for (int i = 0; i < n; i++)
        {
            var f = adf.Frames[i];
            sb.AppendLine($"[sub_resource type=\"AtlasTexture\" id=\"Atlas_{i}\"]");
            sb.AppendLine("atlas = ExtResource(\"1\")");
            sb.AppendLine($"region = Rect2({f.X}, {f.Y}, {f.W}, {f.H})");
            sb.AppendLine();
        }

        sb.AppendLine("[resource]");
        sb.AppendLine("animations = [{");
        sb.Append("\"frames\": [");
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"{{\"duration\": 1.0, \"texture\": SubResource(\"Atlas_{i}\")}}");
        }
        sb.AppendLine("],");
        sb.AppendLine("\"loop\": true,");
        sb.AppendLine("\"name\": &\"all\",");
        sb.AppendLine($"\"speed\": {speed:0.0}");
        sb.AppendLine("}]");

        return sb.ToString();
    }

    /// <summary>Builds a Godot 4 SpriteFrames .tres from multiple compiled animations,
    /// potentially referencing multiple texture sheets. Deduplicates ext_resources by path.</summary>
    public static string Build(IEnumerable<SpriteFramesAnimationSpec> animations)
    {
        var animList = animations.ToList();
        if (animList.Count == 0)
            throw new ArgumentException("Must provide at least one animation.", nameof(animations));

        foreach (var anim in animList)
        {
            if (anim.Frames.Count == 0)
                throw new ArgumentException($"Animation \"{anim.Name}\" must have at least one frame.", nameof(animations));
        }

        var sb = new StringBuilder();

        // Collect unique texture paths in first-seen order
        var seenPaths = new HashSet<string>();
        var texturePaths = new List<string>();
        foreach (var anim in animList)
        {
            foreach (var frame in anim.Frames)
            {
                if (seenPaths.Add(frame.TexturePath))
                    texturePaths.Add(frame.TexturePath);
            }
        }

        sb.AppendLine("[gd_resource type=\"SpriteFrames\" format=3]");
        sb.AppendLine();

        // Emit ext_resources with deterministic ids matching ExtResource references
        var pathToId = new Dictionary<string, string>();
        for (int i = 0; i < texturePaths.Count; i++)
        {
            string id = $"Tex_{i}";
            pathToId[texturePaths[i]] = id;
            sb.AppendLine($"[ext_resource type=\"Texture2D\" path=\"{texturePaths[i]}\" id=\"{id}\"]");
        }
        sb.AppendLine();

        // Emit AtlasTexture sub-resources (one per frame occurrence, no dedup)
        int atlasIndex = 0;
        var allAtlases = new List<string>(); // atlas id per frame
        foreach (var anim in animList)
        {
            foreach (var frame in anim.Frames)
            {
                string atlasId = $"Atlas_{atlasIndex}";
                allAtlases.Add(atlasId);
                sb.AppendLine($"[sub_resource type=\"AtlasTexture\" id=\"{atlasId}\"]");
                sb.AppendLine($"atlas = ExtResource(\"{pathToId[frame.TexturePath]}\")");
                sb.AppendLine($"region = Rect2({frame.Frame.X}, {frame.Frame.Y}, {frame.Frame.W}, {frame.Frame.H})");
                sb.AppendLine();
                atlasIndex++;
            }
        }

        // Emit resource with animations
        sb.AppendLine("[resource]");
        sb.Append("animations = [");

        int frameCursor = 0;
        for (int a = 0; a < animList.Count; a++)
        {
            var anim = animList[a];
            if (a == 0)
            {
                sb.Append("{");
            }
            else
            {
                sb.Append("}, {");
            }

            sb.Append("\"frames\": [");
            for (int f = 0; f < anim.Frames.Count; f++)
            {
                if (f > 0) sb.Append(", ");
                string atlasId = allAtlases[frameCursor];
                sb.Append($"{{\"duration\": 1.0, \"texture\": SubResource(\"{atlasId}\")}}");
                frameCursor++;
            }
            sb.Append("],");
            sb.Append($"\"loop\": {(anim.Loop ? "true" : "false")},");
            sb.Append($"\"name\": &\"{anim.Name}\",");
            sb.Append($"\"speed\": {anim.Speed:0.0}");
        }

        sb.AppendLine("}]");

        return sb.ToString();
    }
}

public sealed record SpriteFrameSpec(int SheetNumber, string TexturePath, Frame Frame);

public sealed record SpriteFramesAnimationSpec(string Name, IReadOnlyList<SpriteFrameSpec> Frames, bool Loop = true, float Speed = 8f)
{
    public static SpriteFramesAnimationSpec FromFrames(string name, int sheetNumber, string texturePath, IReadOnlyList<Frame> frames)
    {
        var specFrames = frames.Select(f => new SpriteFrameSpec(sheetNumber, texturePath, f)).ToList();
        return new SpriteFramesAnimationSpec(name, specFrames);
    }
}
