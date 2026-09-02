using System.IO;

namespace MapEditor.Core;

public sealed class MapExternalChangeException : IOException
{
    public string Path { get; }

    public MapFileRevision ExpectedRevision { get; }

    public MapFileRevision? ActualRevision { get; }

    public MapExternalChangeException(string path, MapFileRevision expectedRevision, MapFileRevision? actualRevision)
        : base($"External change detected at '{path}'.")
    {
        Path = path;
        ExpectedRevision = expectedRevision;
        ActualRevision = actualRevision;
    }
}
