using System;
using System.Globalization;
using System.Text;
using Goose2Client.Logs;

namespace Goose2Client.Logs
{
    public static class LogDetailsFormatter
    {
        private static readonly string[] Keys =
        {
            "Summary", "UTC", "Row ID", "Type Label", "Type ID", "Type Is Integer", "Group", "Other ID Kind",
            "Primary Label", "Primary Kind", "Primary ID", "Primary Name", "Primary Can Quick Filter",
            "Related Label", "Related Kind", "Related ID", "Related Name", "Related Can Quick Filter",
            "Map ID", "Map Name", "Map Can Quick Filter",
            "Raw Player ID", "Raw Player ID Is Integer",
            "Raw Other ID", "Raw Other ID Is Integer",
            "Raw Map ID", "Raw Map ID Is Integer",
            "Raw Map X", "Raw Map X Is Integer",
            "Raw Map Y", "Raw Map Y Is Integer",
            "Original Text"
        };

        public static string Format(LogRow row)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Keys.Length; i++)
            {
                if (i > 0)
                    sb.Append('\n');
                sb.Append(Keys[i]).Append(": ").Append(Value(row, i));
            }
            return sb.ToString();
        }

        public static string FormatDetails(LogRow row) => Format(row);
        public static string FormatClipboard(LogRow row) => Format(row);

        public static string[] TableColumns(LogRow row)
        {
            return new[]
            {
                FormatTableUtc(row.UtcMilliseconds),
                row.EventLabel,
                row.Primary.Label,
                row.Related?.Label ?? "",
                row.Map?.Name ?? "",
                row.Summary
            };
        }

        private static string FormatTableUtc(long unixMs)
            => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

        private static string FormatIsoUtc(long unixMs)
            => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        private static string Value(LogRow row, int key)
        {
            switch (key)
            {
                case 0: return row.Summary;
                case 1: return FormatIsoUtc(row.UtcMilliseconds);
                case 2: return row.RowId.ToString(CultureInfo.InvariantCulture);
                case 3: return row.EventLabel;
                case 4: return row.TypeId.ToString(CultureInfo.InvariantCulture);
                case 5: return Bool(row.TypeIsInteger);
                case 6: return row.EventGroup;
                case 7: return row.OtherIdKind.ToString();
                case 8: return row.Primary.Label;
                case 9: return row.Primary.Kind.ToString();
                case 10: return row.Primary.Id?.ToString(CultureInfo.InvariantCulture) ?? "";
                case 11: return row.Primary.Name;
                case 12: return Bool(row.Primary.CanQuickFilter);
                case 13: return row.Related?.Label ?? "";
                case 14: return row.Related?.Kind.ToString() ?? "";
                case 15: return row.Related?.Id?.ToString(CultureInfo.InvariantCulture) ?? "";
                case 16: return row.Related?.Name ?? "";
                case 17: return row.Related != null ? Bool(row.Related.CanQuickFilter) : "";
                case 18: return row.Map != null ? row.Map.Id.ToString(CultureInfo.InvariantCulture) : "";
                case 19: return row.Map?.Name ?? "";
                case 20: return row.Map != null ? Bool(row.Map.CanQuickFilter) : "";
                case 21: return row.Raw.PlayerId.ToString(CultureInfo.InvariantCulture);
                case 22: return Bool(row.Raw.PlayerIdIsInteger);
                case 23: return row.Raw.OtherId.ToString(CultureInfo.InvariantCulture);
                case 24: return Bool(row.Raw.OtherIdIsInteger);
                case 25: return row.Raw.MapId.ToString(CultureInfo.InvariantCulture);
                case 26: return Bool(row.Raw.MapIdIsInteger);
                case 27: return row.Raw.MapX.ToString(CultureInfo.InvariantCulture);
                case 28: return Bool(row.Raw.MapXIsInteger);
                case 29: return row.Raw.MapY.ToString(CultureInfo.InvariantCulture);
                case 30: return Bool(row.Raw.MapYIsInteger);
                default: return row.OriginalText;
            }
        }

        private static string Bool(bool value) => value ? "true" : "false";
    }
}
