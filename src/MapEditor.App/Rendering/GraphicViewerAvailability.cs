using System;

namespace MapEditor.App.Rendering;

public readonly record struct GraphicViewerAvailability(bool IsAvailable, string? Diagnostic)
{
    public static GraphicViewerAvailability Available { get; } = new(true, null);

    public static GraphicViewerAvailability Unavailable(string diagnostic)
        => new(false, string.IsNullOrWhiteSpace(diagnostic) ? throw new ArgumentException("Diagnostic is required.", nameof(diagnostic)) : diagnostic);
}
