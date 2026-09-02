using System;
using System.IO;

namespace MapEditor.Rendering;

public enum SpriteManifestError
{
    ManifestNotFound,
    ReadFailed,
    MalformedJson,
    InvalidRoot,
    MissingTileSize,
    UnsupportedTileSize,
    MissingSheets,
    InvalidSheetId,
    DuplicateSheetId,
    InvalidSheetFrames,
    InvalidGraphicId,
    DuplicateGraphicId,
    InvalidFrameRect
}

public sealed class SpriteManifestException : IOException
{
    public SpriteManifestError Error { get; }
    public string Path { get; }

    public SpriteManifestException(SpriteManifestError error, string path, string message, Exception? inner = null)
        : base(message, inner)
    {
        Error = error;
        Path = path;
    }
}
