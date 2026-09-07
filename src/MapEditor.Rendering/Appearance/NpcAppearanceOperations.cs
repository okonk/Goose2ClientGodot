using System.Collections.Generic;
using MapEditor.GameData.Rows;

namespace MapEditor.Rendering;

public enum NpcPartSlot
{
    Body,
    Eyes,
    Feet,
    Legs,
    Chest,
    Hair,
    Helm,
    Shield,
    Weapon
}

public readonly record struct NpcPartDestination(int X, int Y, int Width, int Height);

public readonly record struct NpcPartDrawOperation(
    NpcPartSlot Slot,
    AppearancePartKind Kind,
    int PartId,
    SpriteResolutionStatus Status,
    SpriteReference Reference,
    SpriteSourceRect SourceRect,
    string? Diagnostic,
    RgbaValue Tint,
    NpcPartDestination Destination)
{
    public bool IsReady => Status == SpriteResolutionStatus.Ready;
}

public sealed record NpcAppearanceGroup(
    int OccurrenceIndex,
    int TileX,
    int TileY,
    int SortAnchorX,
    int SortAnchorY,
    IReadOnlyList<NpcPartDrawOperation> Parts,
    string? EquipmentDiagnostic);
