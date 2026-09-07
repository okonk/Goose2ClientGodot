using System;
using System.IO;

namespace MapEditor.Rendering;

public enum AppearanceManifestError
{
    ManifestNotFound,
    ReadFailed,
    MalformedJson,
    InvalidRoot,
    MissingVersion,
    UnsupportedVersion,
    MissingParts,
    InvalidPartKind,
    DuplicatePartKind,
    InvalidPartId,
    DuplicatePartId,
    InvalidPartEntry,
    InvalidVariant,
    DuplicateVariant,
    InvalidReference
}

public sealed class AppearanceManifestException : IOException
{
    public AppearanceManifestError Error { get; }
    public string Path { get; }

    public AppearanceManifestException(AppearanceManifestError error, string path, string message, Exception? inner = null)
        : base(message, inner)
    {
        Error = error;
        Path = path;
    }
}
