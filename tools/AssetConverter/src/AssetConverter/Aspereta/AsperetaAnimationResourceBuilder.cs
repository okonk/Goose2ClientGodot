using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;

namespace Goose2.AssetConverter.Aspereta;

public sealed record AsperetaAnimationBuildResult(
    IReadOnlyList<CompiledSpriteFramesResource> Resources,
    IReadOnlyList<string> Diagnostics);

public static class AsperetaAnimationResourceBuilder
{
    // Aspereta facing 0=Up, 1=Right, 2=Down, 3=Left; output direction 0=Left, 1=Down, 2=Right, 3=Up.
    private static readonly int[] SourceFacingForDirection = { 3, 2, 1, 0 };

    private sealed record Clip(SpriteFramesAnimationSpec Spec, AsperetaMotion Motion, int State, int Direction);

    public static AsperetaAnimationBuildResult Build(AsperetaAnimationCatalog catalog)
    {
        var resources = new List<CompiledSpriteFramesResource>();
        var diagnostics = new List<string>(catalog.Diagnostics);

        foreach (var entry in catalog.CompiledEntries.OrderBy(e => (int)e.Type).ThenBy(e => e.Id))
        {
            var slots = catalog.CompiledSlots
                .Where(s => s.Type == entry.Type && s.ResourceId == entry.Id)
                .ToList();
            var resource = BuildResource(entry, slots, diagnostics);
            if (resource is not null)
                resources.Add(resource);
        }

        return new AsperetaAnimationBuildResult(resources, diagnostics);
    }

    private static CompiledSpriteFramesResource? BuildResource(
        AsperetaCompiledAnimation entry,
        IReadOnlyList<AsperetaCompiledSlot> slots,
        List<string> diagnostics)
    {
        int outputId = AsperetaSheets.BodyBase + entry.Id;
        string heightPrefix = $"{entry.Type}-{outputId}-";

        var clips = new List<Clip>();
        for (int motion = 0; motion < 2; motion++)
        {
            var aspMotion = motion == 0 ? AsperetaMotion.Walk : AsperetaMotion.Attack;
            for (int state = 1; state <= 4; state++)
            {
                for (int dir = 0; dir < 4; dir++)
                {
                    var slot = slots.FirstOrDefault(s =>
                        s.Motion == aspMotion && s.State == state && s.Facing == SourceFacingForDirection[dir]);
                    if (slot?.Resolution is not { } resolution)
                        continue;

                    var frames = resolution.Frames
                        .Select(f => new SpriteFrameSpec(f.Sheet, TexturePath(f.Sheet), f.Frame))
                        .ToList();
                    var spec = new SpriteFramesAnimationSpec(
                        $"{ClipBase(aspMotion, state)}-{AnimationNaming.DirectionName((AnimationDirection)dir)}",
                        frames, true, resolution.Fps);
                    clips.Add(new Clip(spec, aspMotion, state, dir));
                }
            }
        }

        if (clips.Count == 0)
        {
            diagnostics.Add($"{entry.Type} {entry.Id}: no resolvable clips; resource omitted");
            return null;
        }

        var animations = new List<SpriteFramesAnimationSpec>(clips.Select(c => c.Spec));

        var idles = new List<Clip>();
        foreach (var clip in clips.Where(c => c.Motion == AsperetaMotion.Walk))
        {
            var idle = Derived($"{IdleBase(clip.State)}-{AnimationNaming.DirectionName((AnimationDirection)clip.Direction)}", clip.Spec, firstFrameOnly: true);
            animations.Add(idle);
            idles.Add(new Clip(idle, clip.Motion, clip.State, clip.Direction));
        }

        for (int dir = 0; dir < 4; dir++)
        {
            string dirName = AnimationNaming.DirectionName((AnimationDirection)dir);
            var walk1 = clips.FirstOrDefault(c => c.Motion == AsperetaMotion.Walk && c.State == 1 && c.Direction == dir);
            if (walk1 is not null)
                animations.Add(Derived($"walk-{dirName}", walk1.Spec, firstFrameOnly: false));
            var attack1 = clips.FirstOrDefault(c => c.Motion == AsperetaMotion.Attack && c.State == 1 && c.Direction == dir);
            if (attack1 is not null)
                animations.Add(Derived($"attack-{dirName}", attack1.Spec, firstFrameOnly: false));
            var idle1 = idles.FirstOrDefault(c => c.State == 1 && c.Direction == dir);
            if (idle1 is not null)
                animations.Add(Derived($"idle-{dirName}", idle1.Spec, firstFrameOnly: false));
        }

        for (int dir = 0; dir < 4; dir++)
        {
            string dirName = AnimationNaming.DirectionName((AnimationDirection)dir);
            var equipped = clips
                .Where(c => c.Motion == AsperetaMotion.Walk && c.State is 2 or 3 or 4 && c.Direction == dir)
                .OrderByDescending(c => c.State)
                .FirstOrDefault();
            if (equipped is null)
                continue;

            animations.Add(Derived($"walk-equip-{dirName}", equipped.Spec, firstFrameOnly: false));
            var idle = idles.First(c => c.State == equipped.State && c.Direction == dir);
            animations.Add(Derived($"idle-equip-{dirName}", idle.Spec, firstFrameOnly: false));
        }

        var heights = new Dictionary<string, int>();
        foreach (var anim in animations)
        {
            int maxHeight = anim.Frames.Max(f => f.Frame.H);
            if (maxHeight != 64)
                heights[$"{heightPrefix}{anim.Name}"] = maxHeight;
        }

        var firstFrames = new Dictionary<string, AnimationFrameInfo>();
        var walkDown = clips.FirstOrDefault(c => c.Motion == AsperetaMotion.Walk && c.State == 1 && c.Direction == 1);
        if (walkDown is not null)
        {
            var first = walkDown.Spec.Frames[0];
            firstFrames[$"{entry.Type}-{outputId}"] =
                new AnimationFrameInfo(first.SheetNumber, first.Frame.Index, first.Frame.W, first.Frame.H);
        }

        return new CompiledSpriteFramesResource(
            entry.Type,
            outputId,
            AnimationNaming.ResourceRelativePath(entry.Type, outputId),
            animations,
            firstFrames,
            heights,
            Array.Empty<string>());
    }

    private static SpriteFramesAnimationSpec Derived(string name, SpriteFramesAnimationSpec source, bool firstFrameOnly)
    {
        var frames = firstFrameOnly ? source.Frames.Take(1).ToList() : source.Frames;
        return new SpriteFramesAnimationSpec(name, frames, source.Loop, source.Speed);
    }

    private static string TexturePath(int sheetNumber) => $"res://Assets/Sprites/sheets/{sheetNumber}.png";

    private static string ClipBase(AsperetaMotion motion, int state)
    {
        string prefix = motion == AsperetaMotion.Walk ? "walk" : "attack";
        return state switch
        {
            1 => $"{prefix}-no-equip",
            2 => $"{prefix}-asp-state-2",
            3 => $"{prefix}-staff",
            _ => $"{prefix}-1hand",
        };
    }

    private static string IdleBase(int state) => state switch
    {
        1 => "idle-no-equip",
        2 => "idle-asp-state-2",
        3 => "idle-staff",
        _ => "idle-1hand",
    };
}
