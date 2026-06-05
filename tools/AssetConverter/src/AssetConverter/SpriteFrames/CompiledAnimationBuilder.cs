using System;
using System.Collections.Generic;
using System.Linq;
using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.SpriteFrames;

/// <summary>
/// Builds compiled character SpriteFrames resource specs and metadata
/// from a <see cref="CompiledAnimation"/> and its resolved ADF sheets.
/// </summary>
public static class CompiledAnimationBuilder
{
    public static CompiledSpriteFramesResource BuildCharacterResource(
        CompiledAnimation compiledAnimation,
        IReadOnlyDictionary<int, AdfFile> adfs)
    {
        var animations = new List<SpriteFramesAnimationSpec>();
        var animationToFirstFrame = new Dictionary<string, AnimationFrameInfo>();
        var animationHeights = new Dictionary<string, int>();
        var warnings = new List<string>();

        string typeFolder = AnimationNaming.TypeFolder(compiledAnimation.Type);
        string relativePath = $"Assets/Sprites/{typeFolder}/{compiledAnimation.Id}/animations.tres";
        string heightPrefix = $"{compiledAnimation.Type}-{compiledAnimation.Id}-";

        for (int animationNumber = 0; animationNumber < 11; animationNumber++)
        {
            AnimationOrder order = (AnimationOrder)animationNumber;
            int fileNumber = compiledAnimation.AnimationFiles[animationNumber];

            if (fileNumber == 0 || !adfs.TryGetValue(fileNumber, out AdfFile? adf))
            {
                for (int d = 0; d < 4; d++)
                {
                    AnimationDirection dir = (AnimationDirection)d;
                    string reason = fileNumber == 0
                        ? $"zero file id for order {order}"
                        : $"missing sheet {fileNumber}";
                    warnings.Add($"{compiledAnimation.Type}-{compiledAnimation.Id} {order} {AnimationNaming.DirectionName(dir)}: {reason}");
                }
                continue;
            }

            string texturePath = $"res://Assets/Sprites/sheets/{fileNumber}.png";

            for (int direction = 0; direction < 4; direction++)
            {
                AnimationDirection dir = (AnimationDirection)direction;
                int animationId = compiledAnimation.AnimationIndexes[direction * 11 + animationNumber];

                if (animationId == 0)
                {
                    warnings.Add($"{compiledAnimation.Type}-{compiledAnimation.Id} {order} {AnimationNaming.DirectionName(dir)}: zero animation id");
                    continue;
                }

                // Resolve frames: try animation-specific, fall back to direction frame
                IReadOnlyList<Frame> frames;
                if (adf.Animations != null && adf.Animations.TryGetValue(animationId, out Animation? anim))
                {
                    frames = anim.Frames;
                }
                else
                {
                    frames = new[] { adf.Frames[(int)dir] };
                }

                // Build the main animation spec
                string clipName = AnimationNaming.ClipName(order, dir);
                var spec = SpriteFramesAnimationSpec.FromFrames(clipName, fileNumber, texturePath, frames);
                animations.Add(spec);

                // Record height metadata if not 64 (scoped by Type-Id for global uniqueness)
                int maxHeight = frames.Max(f => f.H);
                if (maxHeight != 64)
                {
                    animationHeights[$"{heightPrefix}{clipName}"] = maxHeight;
                }

                // Add compatibility aliases
                IReadOnlyList<string> aliases = AnimationNaming.CompatibilityAliases(order, dir);
                foreach (string alias in aliases)
                {
                    var aliasSpec = new SpriteFramesAnimationSpec(alias, spec.Frames);
                    animations.Add(aliasSpec);
                    if (maxHeight != 64)
                    {
                        animationHeights[$"{heightPrefix}{alias}"] = maxHeight;
                    }
                }

                // First-frame metadata: WalkingNoEquip + Down only
                if (order == AnimationOrder.WalkingNoEquip && dir == AnimationDirection.Down)
                {
                    Frame firstFrame = frames[0];
                    animationToFirstFrame[$"{compiledAnimation.Type}-{compiledAnimation.Id}"] =
                        new AnimationFrameInfo(fileNumber, firstFrame.Index, firstFrame.W, firstFrame.H);
                }

                // Idle generation for WalkingNoEquip, WalkingEquip, Mounted
                if (AnimationNaming.TryIdleName(order, dir, out string? idleName))
                {
                    Frame firstFrame = frames[0];
                    var idleFrames = new[] { firstFrame };
                    var idleSpec = SpriteFramesAnimationSpec.FromFrames(idleName, fileNumber, texturePath, idleFrames);
                    animations.Add(idleSpec);

                    int idleHeight = firstFrame.H;
                    if (idleHeight != 64)
                    {
                        animationHeights[$"{heightPrefix}{idleName}"] = idleHeight;
                    }

                    // Simple idle compatibility alias for WalkingNoEquip only
                    if (order == AnimationOrder.WalkingNoEquip)
                    {
                        string simpleIdleName = $"idle-{AnimationNaming.DirectionName(dir)}";
                        var simpleIdleSpec = new SpriteFramesAnimationSpec(simpleIdleName, idleSpec.Frames);
                        animations.Add(simpleIdleSpec);
                        if (idleHeight != 64)
                        {
                            animationHeights[$"{heightPrefix}{simpleIdleName}"] = idleHeight;
                        }
                    }
                }
            }
        }

        return new CompiledSpriteFramesResource(
            compiledAnimation.Type,
            compiledAnimation.Id,
            relativePath,
            animations,
            animationToFirstFrame,
            animationHeights,
            warnings);
    }
}
