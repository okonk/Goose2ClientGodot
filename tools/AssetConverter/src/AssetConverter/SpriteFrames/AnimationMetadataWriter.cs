using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Goose2.AssetConverter.SpriteFrames;

/// <summary>
/// Writes Unity-compatible animation metadata text files.
/// <para>AnimationToFirstFrame.txt: name,fileId,graphicId,width,height</para>
/// <para>AnimationHeights.txt: name,height</para>
/// </summary>
public static class AnimationMetadataWriter
{
    /// <summary>
    /// Merges multiple first-frame dictionaries into one.
    /// Duplicate keys with the same value are allowed; conflicting values throw.
    /// </summary>
    public static Dictionary<string, AnimationFrameInfo> MergeFirstFrames(
        IEnumerable<IReadOnlyDictionary<string, AnimationFrameInfo>> dictionaries)
    {
        var merged = new Dictionary<string, AnimationFrameInfo>();

        foreach (var dict in dictionaries)
        {
            foreach (var kvp in dict)
            {
                if (merged.TryGetValue(kvp.Key, out var existing))
                {
                    if (!existing.Equals(kvp.Value))
                        throw new InvalidOperationException(
                            $"Conflicting values for animation \"{kvp.Key}\" in merged first-frame dictionaries.");
                }
                else
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
        }

        return merged;
    }

    /// <summary>
    /// Merges multiple height dictionaries into one.
    /// Duplicate keys with the same value are allowed; conflicting values throw.
    /// </summary>
    public static Dictionary<string, int> MergeHeights(
        IEnumerable<IReadOnlyDictionary<string, int>> dictionaries)
    {
        var merged = new Dictionary<string, int>();

        foreach (var dict in dictionaries)
        {
            foreach (var kvp in dict)
            {
                if (merged.TryGetValue(kvp.Key, out var existing))
                {
                    if (existing != kvp.Value)
                        throw new InvalidOperationException(
                            $"Conflicting values for animation \"{kvp.Key}\" in merged height dictionaries.");
                }
                else
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
        }

        return merged;
    }

    /// <summary>
    /// Builds the first-frame text with lines sorted by key (ordinal).
    /// Format: name,fileId,graphicId,width,height
    /// </summary>
    public static string BuildFirstFrameText(IReadOnlyDictionary<string, AnimationFrameInfo> frames)
    {
        var lines = frames
            .OrderBy(kvp => kvp.Key, System.StringComparer.Ordinal)
            .Select(kvp => $"{kvp.Key},{kvp.Value.FileId},{kvp.Value.GraphicId},{kvp.Value.Width},{kvp.Value.Height}")
            .ToList();

        return string.Join("\n", lines) + (lines.Count > 0 ? "\n" : "");
    }

    /// <summary>
    /// Builds the heights text with lines sorted by key (ordinal).
    /// Format: name,height
    /// </summary>
    public static string BuildHeightsText(IReadOnlyDictionary<string, int> heights)
    {
        var lines = heights
            .OrderBy(kvp => kvp.Key, System.StringComparer.Ordinal)
            .Select(kvp => $"{kvp.Key},{kvp.Value}")
            .ToList();

        return string.Join("\n", lines) + (lines.Count > 0 ? "\n" : "");
    }

    /// <summary>
    /// Writes both metadata files to the given resources directory.
    /// Creates the directory if it doesn't exist. Overwrites any existing files.
    /// </summary>
    public static void Write(
        string resourcesDir,
        IReadOnlyDictionary<string, AnimationFrameInfo> frames,
        IReadOnlyDictionary<string, int> heights)
    {
        Directory.CreateDirectory(resourcesDir);

        var firstFramePath = Path.Combine(resourcesDir, "AnimationToFirstFrame.txt");
        var heightsPath = Path.Combine(resourcesDir, "AnimationHeights.txt");

        File.WriteAllText(firstFramePath, BuildFirstFrameText(frames));
        File.WriteAllText(heightsPath, BuildHeightsText(heights));
    }
}
