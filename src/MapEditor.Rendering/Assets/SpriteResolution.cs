using System;

namespace MapEditor.Rendering;

public enum SpriteSheetLoadStatus
{
    Success,
    NotFound,
    InvalidData,
    Unreadable
}

public readonly record struct SpriteSheetLoadResult(
    SpriteSheetLoadStatus Status,
    ISpriteSheetImage? Image,
    string? Diagnostic)
{
    public static SpriteSheetLoadResult Success(ISpriteSheetImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new SpriteSheetLoadResult(SpriteSheetLoadStatus.Success, image, null);
    }

    public static SpriteSheetLoadResult Failure(SpriteSheetLoadStatus status, string diagnostic)
    {
        if (status is SpriteSheetLoadStatus.Success)
        {
            throw new ArgumentException("Failure status must not be Success.", nameof(status));
        }

        if (string.IsNullOrEmpty(diagnostic))
        {
            throw new ArgumentException("Diagnostic is required for a failure result.", nameof(diagnostic));
        }

        return new SpriteSheetLoadResult(status, null, diagnostic);
    }
}

public enum SpriteResolutionStatus
{
    Ready,
    Empty,
    UnknownSheet,
    UnknownGraphic,
    MissingSheetFile,
    SheetLoadFailed,
    FrameOutsideSheet,
    AssetsUnavailable
}

public readonly record struct SpriteResolution(
    SpriteResolutionStatus Status,
    SpriteReference Reference,
    ISpriteSheetImage? Image,
    SpriteSourceRect SourceRect,
    string? Diagnostic);
