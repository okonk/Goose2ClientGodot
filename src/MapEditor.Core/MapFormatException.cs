using System.IO;

namespace MapEditor.Core;

public enum MapFormatError
{
    TruncatedHeader,
    UnsupportedEditorVersion,
    InvalidDimensions,
    OversizedDimensions,
    LengthMismatch
}

public sealed class MapFormatException : IOException
{
    public MapFormatError Error { get; }

    public MapFormatException(MapFormatError error, string message) : base(message)
    {
        Error = error;
    }
}
