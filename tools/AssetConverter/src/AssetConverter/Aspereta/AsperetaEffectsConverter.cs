using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;

namespace Goose2.AssetConverter.Aspereta;

public record AsperetaEffectsResult(int EffectsWritten, int Failed, List<string> Failures);

/// <summary>
/// Emits effect SpriteFrames for every Aspereta ADF animation that is not referenced
/// by Aspereta <c>compiled.enc</c>, at offset id <c>GraphicBase + animId</c>
/// (the server remap rewrites spell <c>animation</c> cells to the same offset).
/// Mirrors the Illutia effect loop in <see cref="AnimationBatchConverter"/>.
/// </summary>
/// <remarks>
/// Aspereta stores most animation defs on <c>0.adf</c> with frame indexes that live
/// on other graphic sheets; this converter resolves those cross-sheet references
/// (same approach as <see cref="AsperetaMonsterConverter"/>).
/// </remarks>
public static class AsperetaEffectsConverter
{
    public static AsperetaEffectsResult Convert(
        string asperetaDataDir, string compiledEncPath, string outRoot)
    {
        var failures = new List<string>();
        int written = 0, failed = 0;
        var effectHeights = new Dictionary<string, int>();

        var compiledIds = new HashSet<int>();
        foreach (var anim in AsperetaCompiledEnc.Load(compiledEncPath))
            foreach (int id in anim.Indexes)
                if (id != 0) compiledIds.Add(id);

        var sheets = AsperetaSheets.Load(asperetaDataDir);

        // Global frame index -> (injected sheet number, frame) for cross-sheet resolve.
        var frameMap = new Dictionary<int, (int NewSheetNumber, Frame Frame)>();
        var animDefs = new Dictionary<int, Animation>();
        foreach (var (_, sheet) in sheets)
        {
            foreach (var frame in sheet.Adf.Frames)
                frameMap[frame.Index] = (sheet.NewSheetNumber, frame);

            if (sheet.Adf.Animations is null)
                continue;
            foreach (var (animId, anim) in sheet.Adf.Animations)
                animDefs[animId] = anim;
        }

        foreach (var (animId, animation) in animDefs)
        {
            if (compiledIds.Contains(animId))
                continue;

            string effectId = (AsperetaSheets.GraphicBase + animId).ToString();
            string relativePath = $"Assets/Sprites/Effects/{effectId}/animations.tres";
            string fullPath = Path.Combine(outRoot, relativePath);

            try
            {
                if (!TryResolveFrames(animId, animation, frameMap,
                        out int sheetNumber, out var frames, out string? error))
                {
                    throw new InvalidOperationException(error ?? "failed to resolve frames");
                }

                string texturePath = $"res://Assets/Sprites/sheets/{sheetNumber}.png";
                var spec = SpriteFramesAnimationSpec.FromFrames(
                    effectId, sheetNumber, texturePath, frames);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, SpriteFramesWriter.Build(new[] { spec }));
                written++;

                int maxHeight = frames.Max(f => f.H);
                if (maxHeight != 64)
                    effectHeights[effectId] = maxHeight;
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"Effect {effectId}: failed to write {relativePath}: {ex.Message}");
            }
        }

        if (effectHeights.Count > 0)
        {
            var resourcesDir = Path.Combine(outRoot, "Assets", "Resources");
            // SAFE merge: preserve any existing metadata already under outRoot.
            // AnimationMetadataWriter.Write overwrites both files, so re-load first.
            var existingHeights = LoadHeights(Path.Combine(resourcesDir, "AnimationHeights.txt"));
            var existingFirst = LoadFirstFrames(
                Path.Combine(resourcesDir, "AnimationToFirstFrame.txt"));
            var mergedHeights = AnimationMetadataWriter.MergeHeights(
                new[] { existingHeights, effectHeights });
            AnimationMetadataWriter.Write(resourcesDir, existingFirst, mergedHeights);
        }

        return new AsperetaEffectsResult(written, failed, failures);
    }

    /// <summary>
    /// Resolves an Aspereta animation's frame ids against the global frame map.
    /// All frames must live on a single injected sheet (same constraint as monsters).
    /// </summary>
    private static bool TryResolveFrames(
        int animId,
        Animation anim,
        IReadOnlyDictionary<int, (int NewSheetNumber, Frame Frame)> frameMap,
        out int sheetNumber,
        out List<Frame> frames,
        out string? error)
    {
        sheetNumber = 0;
        frames = new List<Frame>();
        error = null;

        IReadOnlyList<int> fids;
        if (anim.SourceFrameIds is { Count: > 0 })
            fids = anim.SourceFrameIds;
        else if (anim.Frames.Count > 0)
            fids = anim.Frames.Select(f => f.Index).ToList();
        else
        {
            error = $"animation {animId} has no frame ids";
            return false;
        }

        int sheetForAnim = 0;
        foreach (int fid in fids)
        {
            if (!frameMap.TryGetValue(fid, out var hit))
            {
                error = $"animation {animId}: frame {fid} not found in any sheet";
                return false;
            }
            if (sheetForAnim != 0 && sheetForAnim != hit.NewSheetNumber)
            {
                error = $"animation {animId}: frames span sheets {sheetForAnim} and {hit.NewSheetNumber}";
                return false;
            }
            sheetForAnim = hit.NewSheetNumber;
            frames.Add(hit.Frame);
        }

        if (sheetForAnim == 0 || frames.Count == 0)
        {
            error = $"animation {animId}: no sheet for frames";
            return false;
        }

        sheetNumber = sheetForAnim;
        return true;
    }

    /// <summary>Parses <c>name,height</c> lines. Missing/malformed lines are skipped.</summary>
    private static Dictionary<string, int> LoadHeights(string path)
    {
        var result = new Dictionary<string, int>();
        if (!File.Exists(path))
            return result;

        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var parts = line.Split(',');
            if (parts.Length < 2)
                continue;
            if (int.TryParse(parts[1], out int height))
                result[parts[0]] = height;
        }
        return result;
    }

    /// <summary>
    /// Parses <c>name,fileId,graphicId,width,height</c> lines.
    /// Missing/malformed lines are skipped.
    /// </summary>
    private static Dictionary<string, AnimationFrameInfo> LoadFirstFrames(string path)
    {
        var result = new Dictionary<string, AnimationFrameInfo>();
        if (!File.Exists(path))
            return result;

        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var parts = line.Split(',');
            if (parts.Length < 5)
                continue;
            if (int.TryParse(parts[1], out int fileId)
                && int.TryParse(parts[2], out int graphicId)
                && int.TryParse(parts[3], out int width)
                && int.TryParse(parts[4], out int height))
            {
                result[parts[0]] = new AnimationFrameInfo(fileId, graphicId, width, height);
            }
        }
        return result;
    }
}
