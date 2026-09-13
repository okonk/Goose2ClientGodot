using System;
using MapEditor.Core;

namespace MapEditor.App.ViewModels;

internal sealed class TerrainEditorItemViewModel
{
    public Guid Id { get; }

    public string Name { get; }

    public TerrainColor Swatch => Definition.DisplayColor;

    public bool HasColorOverride => Definition.ColorOverride is not null;

    public string ColorOverrideText => Definition.ColorOverride is { } color ? FormatColor(color) : string.Empty;

    internal TerrainDefinition Definition { get; }

    internal TerrainEditorItemViewModel(TerrainDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Id = definition.Id;
        Name = definition.Name;
    }

    internal static string FormatColor(TerrainColor color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
