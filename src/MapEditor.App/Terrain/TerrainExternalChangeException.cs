using System.IO;
using MapEditor.Rendering;

namespace MapEditor.App.Terrain;

public sealed class TerrainExternalChangeException : IOException
{
    public string Path { get; }

    public TerrainFileRevision ExpectedRevision { get; }

    public TerrainFileRevision? ActualRevision { get; }

    public TerrainExternalChangeException(string path, TerrainFileRevision expectedRevision, TerrainFileRevision? actualRevision)
        : base($"External change detected at '{path}'.")
    {
        Path = path;
        ExpectedRevision = expectedRevision;
        ActualRevision = actualRevision;
    }
}
