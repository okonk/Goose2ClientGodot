using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;

namespace Goose2.AssetConverter.Aspereta;

public record AsperetaEffectsResult(
    int EffectsWritten, int Failed, List<string> Failures, int SkippedOutOfRange = 0);

/// <summary>
/// Emits effect SpriteFrames for the Aspereta ADF animations that are emotes or spell
/// effects, at offset id <c>GraphicBase + animId</c> (the server remap rewrites spell
/// <c>animation</c> cells to the same offset).
/// </summary>
/// <remarks>
/// <para>
/// Selection is by id range (see <see cref="EmoteIdMax"/> and <see cref="SpellIdMin"/>).
/// "Every animation not referenced by <c>compiled.enc</c>" over-selects: it also picks up
/// ~41 stray defs in the 55xxx/95xxx/96xxx/97xxx character buckets that no spell or emote
/// ever plays. The sheet-level rule <see cref="AnimationBatchConverter"/> uses does not work
/// here, because Aspereta parks nearly every animation def on <c>0.adf</c>.
/// </para>
/// <para>
/// Aspereta stores those defs with frame indexes that live on other graphic sheets;
/// this converter resolves the cross-sheet references (same approach as
/// <see cref="AsperetaMonsterConverter"/>).
/// </para>
/// </remarks>
public static class AsperetaEffectsConverter
{
    /// <summary>Emote animation ids: <c>0..999</c>, emitted as <c>700000..700999</c>.</summary>
    public const int EmoteIdMax = 999;

    /// <summary>Spell effect animation ids: <c>115000..115999</c>, emitted as <c>815xxx</c>.</summary>
    public const int SpellIdMin = 115000;

    /// <inheritdoc cref="SpellIdMin"/>
    public const int SpellIdMax = 115999;

    public static bool IsEffectId(int animId) =>
        animId <= EmoteIdMax || (animId >= SpellIdMin && animId <= SpellIdMax);

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

        int skippedOutOfRange = 0;
        foreach (var (animId, animation) in animDefs)
        {
            if (!IsEffectId(animId))
            {
                if (!compiledIds.Contains(animId))
                    skippedOutOfRange++;
                continue;
            }

            // Holds across the current data; surfaced rather than silently dropped so a
            // broken range assumption cannot cost us a spell animation unnoticed.
            if (compiledIds.Contains(animId))
            {
                failures.Add($"Effect {AsperetaSheets.GraphicBase + animId}: animation {animId} is " +
                    "in the emote/spell id range but compiled.enc claims it; skipped");
                continue;
            }

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
            try
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
            catch (Exception ex)
            {
                failed++;
                failures.Add($"Metadata merge/write failed: {ex.GetType().Name} {ex.Message}");
            }
        }

        return new AsperetaEffectsResult(written, failed, failures, skippedOutOfRange);
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
