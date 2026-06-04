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
}
