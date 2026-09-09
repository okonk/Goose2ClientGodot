using System;
using System.IO;

namespace MapEditor.Core.Terrain;

public enum TerrainCatalogError
{
    MalformedJson,
    InvalidRoot,
    UnsupportedSchemaVersion,
    MissingProperty,
    DuplicateProperty,
    UnknownProperty,
    InvalidPropertyType,
    NullNotAllowed,
    InvalidEnum,
    NumberOutOfRange,
    InvalidRelationship
}

public sealed class TerrainCatalogException : FormatException
{
    public TerrainCatalogError Error { get; }
    public string SourcePath { get; }

    public TerrainCatalogException(TerrainCatalogError error, string sourcePath, string message, Exception? inner = null)
        : base(message, inner)
    {
        Error = error;
        SourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
    }
}
