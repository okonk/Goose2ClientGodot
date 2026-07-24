using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;

namespace Goose2.AssetConverter.Aspereta;

/// <summary>
/// Synthesizes Illutia-style 4×11 compiled body resources for Aspereta monsters
/// (body id &gt; 100) from their 4×8 compiled.enc entries (walk + attack only).
/// Output body ids are offset by <see cref="AsperetaSheets.BodyBase"/>.
/// </summary>
public static class AsperetaMonsterConverter
{
    /// <summary>
    /// Maps Illutia direction (Left=0, Down=1, Right=2, Up=3) to
    /// Aspereta facing (Up=0, Right=1, Down=2, Left=3).
    /// </summary>
    private static readonly int[] AspFacingForIllutiaDir = { 3, 2, 1, 0 };

    public static List<CompiledSpriteFramesResource> BuildResources(
        IReadOnlyList<AsperetaCompiledAnimation> entries,
        IReadOnlyDictionary<int, AsperetaSheet> sheets,
        out List<string> errors)
    {
        errors = new List<string>();

        // Aspereta animations (usually in 0.adf) reference frames by global index that
        // typically live on other graphic sheets. Resolve cross-sheet here.
        var frameMap = new Dictionary<int, (int NewSheetNumber, Frame Frame)>();
        var animDefs = new Dictionary<int, Animation>();
        var renumbered = new Dictionary<int, AdfFile>();

        foreach (var (_, sheet) in sheets)
        {
            var adf = sheet.Adf;
            adf.FileNumber = sheet.NewSheetNumber;

            if (!renumbered.TryGetValue(sheet.NewSheetNumber, out var target))
            {
                target = adf;
                // Ensure we can attach resolved animations without clobbering null.
                target.Animations ??= new Dictionary<int, Animation>();
                renumbered[sheet.NewSheetNumber] = target;
            }

            foreach (var frame in adf.Frames)
                frameMap[frame.Index] = (sheet.NewSheetNumber, frame);

            if (adf.Animations is null)
                continue;
            foreach (var (animId, anim) in adf.Animations)
                animDefs[animId] = anim;
        }

        // animId -> sheet that owns the resolved frames (for texture path / AnimationFiles)
        var animToFile = new Dictionary<int, int>();

        foreach (var (animId, anim) in animDefs)
        {
            if (!TryResolveAnimation(animId, anim, frameMap, renumbered, out int sheetNumber, out var resolved, out _))
                continue;

            renumbered[sheetNumber].Animations![animId] = resolved;
            animToFile[animId] = sheetNumber;
        }

        // Some compiled.enc slots point at a single frame index rather than an animation id.
        void EnsureFrameAsAnim(int frameOrAnimId)
        {
            if (frameOrAnimId == 0 || animToFile.ContainsKey(frameOrAnimId))
                return;
            if (!frameMap.TryGetValue(frameOrAnimId, out var hit))
                return;

            var single = new Animation(frameOrAnimId) { Frames = { hit.Frame }, SourceFrameIds = new List<int> { frameOrAnimId } };
            renumbered[hit.NewSheetNumber].Animations![frameOrAnimId] = single;
            animToFile[frameOrAnimId] = hit.NewSheetNumber;
        }

        var resources = new List<CompiledSpriteFramesResource>();
        foreach (var entry in entries.Where(e => e.Type == AnimationType.Body && e.Id > 100))
        {
            // Pre-register any frame-as-animation ids this monster uses.
            for (int facing = 0; facing < 4; facing++)
            {
                EnsureFrameAsAnim(entry.Walk(facing));
                EnsureFrameAsAnim(entry.Attack(facing));
            }

            var ca = new CompiledAnimation(AnimationType.Body, AsperetaSheets.BodyBase + entry.Id);

            bool ok = FillOrder(ca, AnimationOrder.WalkingNoEquip, entry.Walk, animToFile, errors, entry.Id)
                    & FillOrder(ca, AnimationOrder.AttackNoEquip, entry.Attack, animToFile, errors, entry.Id);
            if (!ok)
                continue;

            resources.Add(CompiledAnimationBuilder.BuildCharacterResource(ca, renumbered));
        }
        return resources;
    }

    private static bool TryResolveAnimation(
        int animId,
        Animation anim,
        IReadOnlyDictionary<int, (int NewSheetNumber, Frame Frame)> frameMap,
        IReadOnlyDictionary<int, AdfFile> renumbered,
        out int sheetNumber,
        out Animation resolved,
        out string? error)
    {
        sheetNumber = 0;
        resolved = anim;
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

        var frames = new List<Frame>(fids.Count);
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

        if (sheetForAnim == 0 || !renumbered.ContainsKey(sheetForAnim))
        {
            error = $"animation {animId}: no sheet for frames";
            return false;
        }

        sheetNumber = sheetForAnim;
        resolved = new Animation(animId)
        {
            Frames = frames,
            SourceFrameIds = fids.ToList(),
        };
        return true;
    }

    private static bool FillOrder(
        CompiledAnimation ca,
        AnimationOrder order,
        Func<int, int> aspIndex,
        IReadOnlyDictionary<int, int> animToFile,
        List<string> errors,
        int aspId)
    {
        int sheetForOrder = 0;
        for (int dir = 0; dir < 4; dir++)
        {
            int animId = aspIndex(AspFacingForIllutiaDir[dir]);
            ca.AnimationIndexes[dir * 11 + (int)order] = animId;
            if (animId == 0)
                continue;

            if (!animToFile.TryGetValue(animId, out int sheet))
            {
                errors.Add($"monster {aspId} {order}: animation {animId} not found in any sheet");
                return false;
            }
            if (sheetForOrder != 0 && sheetForOrder != sheet)
            {
                errors.Add($"monster {aspId} {order}: directions span sheets {sheetForOrder} and {sheet}");
                return false;
            }
            sheetForOrder = sheet;
        }
        ca.AnimationFiles[(int)order] = sheetForOrder;
        return true;
    }
}
