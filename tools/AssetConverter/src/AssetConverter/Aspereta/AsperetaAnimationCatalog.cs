using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.Aspereta;

public enum AsperetaMotion
{
    Walk,
    Attack
}

public sealed record AsperetaResolvedFrame(int Sheet, Frame Frame);

public sealed record AsperetaResolvedAnimation(
    int SourceId, int Fps, bool DirectFrame, IReadOnlyList<AsperetaResolvedFrame> Frames);

public sealed record AsperetaCompiledSlot(
    AnimationType Type, int ResourceId, AsperetaMotion Motion, int State, int Facing,
    int ReferenceId, AsperetaResolvedAnimation? Resolution);

public sealed class AsperetaAnimationCatalog
{
    public IReadOnlyDictionary<int, AsperetaSheet> Sheets { get; }
    public IReadOnlyList<AsperetaCompiledAnimation> CompiledEntries { get; }
    public IReadOnlyDictionary<int, AsperetaResolvedAnimation> Resolved { get; }
    public IReadOnlyList<AsperetaCompiledSlot> CompiledSlots { get; }
    public IReadOnlySet<int> ClaimedIds { get; }
    public IReadOnlyList<int> UnclaimedResolvedIds { get; }
    public IReadOnlyList<string> Diagnostics { get; }

    private AsperetaAnimationCatalog(
        IReadOnlyDictionary<int, AsperetaSheet> sheets,
        IReadOnlyList<AsperetaCompiledAnimation> compiledEntries,
        IReadOnlyDictionary<int, AsperetaResolvedAnimation> resolved,
        IReadOnlyList<AsperetaCompiledSlot> compiledSlots,
        IReadOnlySet<int> claimedIds,
        IReadOnlyList<int> unclaimedResolvedIds,
        IReadOnlyList<string> diagnostics)
    {
        Sheets = sheets;
        CompiledEntries = compiledEntries;
        Resolved = resolved;
        CompiledSlots = compiledSlots;
        ClaimedIds = claimedIds;
        UnclaimedResolvedIds = unclaimedResolvedIds;
        Diagnostics = diagnostics;
    }

    public static AsperetaAnimationCatalog Load(string dataDir, string compiledEncPath)
    {
        var sheets = AsperetaSheets.Load(dataDir);
        var entries = AsperetaCompiledEnc.Load(compiledEncPath);

        var frames = new Dictionary<int, (int Sheet, Frame Frame, int SourceFile)>();
        foreach (var (fileNumber, sheet) in sheets)
            foreach (var frame in sheet.Adf.Frames)
            {
                if (frames.TryGetValue(frame.Index, out var owner) && owner.SourceFile != fileNumber)
                    throw new InvalidOperationException(
                        $"duplicate Aspereta frame {frame.Index} in {owner.SourceFile}.adf and {fileNumber}.adf");
                frames[frame.Index] = (sheet.NewSheetNumber, frame, fileNumber);
            }

        var definitions = new Dictionary<int, (Animation Animation, int SourceFile)>();
        foreach (var path in NumericAdfPaths(dataDir))
        {
            AdfFile adf;
            try { adf = AsperetaAdf.Load(path); }
            catch { continue; }
            if (adf.Type != AdfType.Graphic || adf.Animations is null) continue;
            foreach (var (animId, animation) in adf.Animations)
            {
                if (definitions.TryGetValue(animId, out var def) && def.SourceFile != adf.FileNumber)
                    throw new InvalidOperationException(
                        $"duplicate Aspereta animation {animId} in {def.SourceFile}.adf and {adf.FileNumber}.adf");
                definitions[animId] = (animation, adf.FileNumber);
            }
        }

        var resolved = new Dictionary<int, AsperetaResolvedAnimation>();
        var definitionFailures = new Dictionary<int, string>();
        foreach (var (animId, (animation, _)) in definitions)
        {
            if (TryResolveDefinition(animId, animation, frames, out var resolvedAnimation, out var reason))
                resolved[animId] = resolvedAnimation;
            else
                definitionFailures[animId] = reason;
        }

        var slots = new List<AsperetaCompiledSlot>();
        foreach (var entry in entries)
            for (int slot = 0; slot < entry.Indexes.Length; slot++)
            {
                int reference = entry.Indexes[slot];
                if (reference == 0) continue;
                int slotBase = slot % 16;
                slots.Add(new AsperetaCompiledSlot(
                    entry.Type, entry.Id,
                    slot < 16 ? AsperetaMotion.Walk : AsperetaMotion.Attack,
                    slotBase % 4 + 1, slotBase / 4, reference, null));
            }

        var claimed = new HashSet<int>();
        var resolvedSlots = new List<AsperetaCompiledSlot>(slots.Count);
        foreach (var slot in slots)
        {
            claimed.Add(slot.ReferenceId);
            if (resolved.TryGetValue(slot.ReferenceId, out var definition))
            {
                resolvedSlots.Add(slot with { Resolution = definition });
                continue;
            }
            if (frames.TryGetValue(slot.ReferenceId, out var frame))
            {
                var direct = new AsperetaResolvedAnimation(
                    slot.ReferenceId, 8, true,
                    new[] { new AsperetaResolvedFrame(frame.Sheet, frame.Frame) });
                resolved[slot.ReferenceId] = direct;
                resolvedSlots.Add(slot with { Resolution = direct });
                continue;
            }
            resolvedSlots.Add(slot);
        }

        var diagnostics = new List<string>();
        var reported = new HashSet<int>();
        foreach (var slot in resolvedSlots)
        {
            if (slot.Resolution is not null || !reported.Add(slot.ReferenceId)) continue;
            if (definitionFailures.TryGetValue(slot.ReferenceId, out var reason))
                diagnostics.Add($"{slot.Type} {slot.ResourceId} {slot.Motion} facing {slot.Facing} state {slot.State}: {reason}");
            else
                diagnostics.Add($"{slot.Type} {slot.ResourceId} {slot.Motion} facing {slot.Facing} state {slot.State}: reference {slot.ReferenceId} not found in any sheet");
        }
        foreach (var (animId, reason) in definitionFailures)
            if (!claimed.Contains(animId))
                diagnostics.Add(reason);

        var unclaimed = resolved.Keys.Where(id => !claimed.Contains(id)).OrderBy(id => id).ToList();

        return new AsperetaAnimationCatalog(
            sheets, entries, new SortedDictionary<int, AsperetaResolvedAnimation>(resolved),
            resolvedSlots, claimed, unclaimed, diagnostics);
    }

    private static bool TryResolveDefinition(
        int animId,
        Animation animation,
        IReadOnlyDictionary<int, (int Sheet, Frame Frame, int SourceFile)> frames,
        out AsperetaResolvedAnimation resolved,
        out string reason)
    {
        resolved = null!;
        reason = null!;

        IReadOnlyList<int> frameIds;
        if (animation.SourceFrameIds is { Count: > 0 })
            frameIds = animation.SourceFrameIds;
        else if (animation.Frames.Count > 0)
            frameIds = animation.Frames.Select(f => f.Index).ToList();
        else
        {
            reason = $"animation {animId} has no frame ids";
            return false;
        }

        var resolvedFrames = new List<AsperetaResolvedFrame>(frameIds.Count);
        foreach (var frameId in frameIds)
        {
            if (!frames.TryGetValue(frameId, out var hit))
            {
                reason = $"animation {animId}: frame {frameId} not found in any sheet";
                return false;
            }
            resolvedFrames.Add(new AsperetaResolvedFrame(hit.Sheet, hit.Frame));
        }

        int interval = animation.Interval ?? 0;
        resolved = new AsperetaResolvedAnimation(animId, interval > 0 ? interval * 2 : 8, false, resolvedFrames);
        return true;
    }

    private static List<string> NumericAdfPaths(string dataDir)
    {
        var paths = new List<(int Number, string Path)>();
        foreach (var path in Directory.EnumerateFiles(dataDir, "*.adf"))
            if (int.TryParse(Path.GetFileNameWithoutExtension(path), out var number))
                paths.Add((number, path));
        paths.Sort(static (a, b) => a.Number.CompareTo(b.Number));
        return paths.Select(p => p.Path).ToList();
    }
}
