using System.Collections.Generic;

namespace MapEditor.GameData.Validation;

public readonly record struct ValidationIssue(string Code, string Message);

public sealed record ValidationResult(IReadOnlyList<ValidationIssue> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class ValidationCodes
{
    public const string SpawnNpcNotFound = "spawn-npc-not-found";
    public const string SpawnOutOfBounds = "spawn-out-of-bounds";
    public const string WarpOutOfBounds = "warp-out-of-bounds";
    public const string WarpDuplicateSource = "warp-duplicate-source";
    public const string WarpDestinationMapNotFound = "warp-dest-map-not-found";
    public const string WarpDestinationNumericRange = "warp-dest-numeric-range";
    public const string WarpDestinationOutOfBounds = "warp-dest-out-of-bounds";
}
