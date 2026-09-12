using System;
using System.IO;

namespace MapEditor.Rendering;

public enum GraphicAnimationManifestError
{
    ManifestNotFound,
    ReadFailed,
    MalformedJson,
    InvalidRoot,
    UnsupportedVersion,
    MissingSheets,
    InvalidSheetId,
    DuplicateSheetId,
    InvalidSheetEntry,
    MissingCategories,
    InvalidCategories,
    InvalidCategoryName,
    InvalidCategoryMapping,
    DuplicateCategoryMapping,
    MissingAnimations,
    InvalidAnimation,
    DuplicateAnimation,
    EmptyAnimationFrames,
    InvalidFrame,
    EmptyFrameGraphic
}

public sealed class GraphicAnimationManifestException : IOException
{
    public GraphicAnimationManifestError Error { get; }
    public string Path { get; }

    public GraphicAnimationManifestException(GraphicAnimationManifestError error, string path, string message, Exception? inner = null)
        : base(message, inner)
    {
        Error = error;
        Path = path;
    }
}
