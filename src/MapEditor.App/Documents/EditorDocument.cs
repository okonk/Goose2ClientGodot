using MapEditor.Core;

namespace MapEditor.App.Documents;

internal sealed class EditorDocument
{
    internal EditorDocument(MapEditSession session, string? path, MapFileRevision? revision)
    {
        Session = session;
        Path = path;
        Revision = revision;
    }

    internal MapEditSession Session { get; }

    internal string? Path { get; }

    internal MapFileRevision? Revision { get; }
}
