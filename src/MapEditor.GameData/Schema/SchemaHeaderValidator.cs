using System;
using System.Collections.Generic;

namespace MapEditor.GameData.Schema;

public readonly record struct HeaderMismatch(int ColumnIndex, string Expected, string? Actual);

public static class SchemaHeaderValidator
{
    public static IReadOnlyList<HeaderMismatch> Validate(
        SheetSchema schema,
        IReadOnlyList<string?> actualHeaders)
    {
        var mismatches = new List<HeaderMismatch>();
        var columns = schema.Columns;
        for (var i = 0; i < columns.Count; i++)
        {
            var actual = i < actualHeaders.Count ? actualHeaders[i] : null;
            if (actual is null || !string.Equals(actual, columns[i].Header, StringComparison.Ordinal))
            {
                mismatches.Add(new HeaderMismatch(i, columns[i].Header, actual));
            }
        }
        return mismatches.AsReadOnly();
    }
}
