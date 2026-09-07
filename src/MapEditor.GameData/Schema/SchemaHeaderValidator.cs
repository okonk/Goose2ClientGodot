using System;
using System.Collections.Generic;
using System.Text;

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
            if (actual is null || !string.Equals(Canonicalize(actual), Canonicalize(columns[i].Header), StringComparison.Ordinal))
            {
                mismatches.Add(new HeaderMismatch(i, columns[i].Header, actual));
            }
        }
        return mismatches.AsReadOnly();
    }

    // Workbook headers are human-maintained and drift cosmetically ("see_invisible" vs "see invisible").
    private static string Canonicalize(string header)
    {
        var builder = new StringBuilder(header.Length);
        var pendingSpace = false;
        foreach (var ch in header)
        {
            if (ch == '_' || char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
            }
            else
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }
                pendingSpace = false;
                builder.Append(ch);
            }
        }
        return builder.ToString();
    }
}
