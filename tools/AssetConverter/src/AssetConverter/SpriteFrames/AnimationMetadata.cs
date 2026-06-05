using System.Collections.Generic;
using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.SpriteFrames;

/// <summary>
/// Describes the first frame of a walking-no-equip-down animation for a character part.
/// Used by the game client to locate the representative sprite region.
/// </summary>
public sealed record AnimationFrameInfo(int FileId, int GraphicId, int Width, int Height);

/// <summary>
/// Complete resource specification for a single character part's SpriteFrames,
/// including all animation clips, metadata dictionaries, and any warnings encountered.
/// </summary>
public sealed record CompiledSpriteFramesResource(
    AnimationType Type,
    int Id,
    string RelativeOutputPath,
    IReadOnlyList<SpriteFramesAnimationSpec> Animations,
    IReadOnlyDictionary<string, AnimationFrameInfo> AnimationToFirstFrame,
    IReadOnlyDictionary<string, int> AnimationHeights,
    IReadOnlyList<string> Warnings);
