using System;

namespace MapEditor.Core;

public enum MapValidationError
{
    SheetOutOfRange
}

public sealed class MapValidationException : Exception
{
    public MapValidationError Error { get; }

    public MapValidationException(MapValidationError error, string message) : base(message)
    {
        Error = error;
    }
}
