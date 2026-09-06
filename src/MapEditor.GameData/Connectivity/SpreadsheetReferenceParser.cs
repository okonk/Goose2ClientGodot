using System;

namespace MapEditor.GameData.Connectivity;

public readonly record struct SpreadsheetReference(string Id, string CanonicalUrl);

public static class SpreadsheetReferenceParser
{
    private const string Host = "docs.google.com";
    private const string CanonicalPrefix = "https://docs.google.com/spreadsheets/d/";

    public static bool TryParse(string? value, out SpreadsheetReference reference)
    {
        reference = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (!uri.Host.Equals(Host, StringComparison.Ordinal))
        {
            return false;
        }

        // Structural validation must run on the escaped path; Uri.AbsolutePath decodes percent-encoding.
        var parts = uri.GetComponents(UriComponents.Path, UriFormat.SafeUnescaped).Split('/');
        if (parts.Length is not (3 or 4 or 5))
        {
            return false;
        }

        if (!parts[0].Equals("spreadsheets", StringComparison.Ordinal) ||
            !parts[1].Equals("d", StringComparison.Ordinal))
        {
            return false;
        }

        if (parts.Length == 4)
        {
            if (parts[3] is not ("" or "edit"))
            {
                return false;
            }
        }
        else if (parts.Length == 5 && (parts[3] != "edit" || parts[4].Length != 0))
        {
            return false;
        }

        var id = parts[2];
        if (!IsValidId(id))
        {
            return false;
        }

        reference = new SpreadsheetReference(id, CanonicalPrefix + id);
        return true;
    }

    public static SpreadsheetReference Parse(string value)
    {
        if (TryParse(value, out var reference))
        {
            return reference;
        }

        throw new FormatException($"'{value}' is not a valid Google spreadsheet URL.");
    }

    private static bool IsValidId(string id)
    {
        if (id.Length == 0)
        {
            return false;
        }

        foreach (var ch in id)
        {
            if (ch is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
