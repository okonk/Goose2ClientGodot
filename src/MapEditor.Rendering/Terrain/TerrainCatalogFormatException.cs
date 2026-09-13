using System;

namespace MapEditor.Rendering;

public sealed class TerrainCatalogFormatException : Exception
{
    public string Path { get; }

    public TerrainCatalogFormatException(string message, string path, Exception? inner = null)
        : base($"{message} ({path})", inner)
    {
        Path = path;
    }
}
