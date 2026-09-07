using System.Collections.Generic;

namespace MapEditor.GameData.Replacement;

public readonly record struct RemoteRow<T>(int WorksheetRowNumber, T Value);

public readonly record struct RowDelete(int WorksheetRowNumber);

public readonly record struct RowInsert(IReadOnlyList<string?> CellValues);

public sealed record ReplacementPlan(
    string Sheet,
    IReadOnlyList<RowDelete> Deletes,
    IReadOnlyList<RowInsert> Inserts);
