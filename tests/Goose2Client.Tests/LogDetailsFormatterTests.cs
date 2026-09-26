using System;
using System.Globalization;
using System.Linq;
using Goose2Client.Logs;
using Xunit;

namespace Goose2Client.Tests
{
    public class LogDetailsFormatterTests
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

        private static LogRow FullRow()
        {
            return new LogRow(
                long.MaxValue,
                1700000000123L,
                -12L,
                false,
                "Ban",
                "GM Actions",
                LogOtherIdKind.Guild,
                new LogRowEntity("Player", LogEntityKind.Player, long.MinValue, "Zed", true),
                new LogRowEntity("Guild", LogEntityKind.Guild, 7L, "G", false),
                new LogRowMap(3L, "Home", true),
                new LogRowRaw(long.MaxValue, false, long.MinValue, true, 5L, true, 6L, false, 7L, true),
                "sum",
                "text");
        }

        [Fact]
        public void FormatProducesExactThirtyTwoKeysInExactOrder()
        {
            string text = LogDetailsFormatter.Format(FullRow());
            var lines = text.Split('\n');
            Assert.Equal(32, lines.Length);
            Assert.DoesNotContain('\r', text);
            for (int i = 0; i < 32; i++)
                Assert.StartsWith(Keys[i] + ": ", lines[i]);
            Assert.Equal("Summary: sum", lines[0]);
            Assert.Equal("UTC: 2023-11-14T22:13:20.123Z", lines[1]);
            Assert.Equal("Row ID: 9223372036854775807", lines[2]);
            Assert.Equal("Type Label: Ban", lines[3]);
            Assert.Equal("Type ID: -12", lines[4]);
            Assert.Equal("Type Is Integer: false", lines[5]);
            Assert.Equal("Group: GM Actions", lines[6]);
            Assert.Equal("Other ID Kind: Guild", lines[7]);
            Assert.Equal("Primary Label: Player", lines[8]);
            Assert.Equal("Primary Kind: Player", lines[9]);
            Assert.Equal("Primary ID: -9223372036854775808", lines[10]);
            Assert.Equal("Primary Name: Zed", lines[11]);
            Assert.Equal("Primary Can Quick Filter: true", lines[12]);
            Assert.Equal("Related Label: Guild", lines[13]);
            Assert.Equal("Related Kind: Guild", lines[14]);
            Assert.Equal("Related ID: 7", lines[15]);
            Assert.Equal("Related Name: G", lines[16]);
            Assert.Equal("Related Can Quick Filter: false", lines[17]);
            Assert.Equal("Map ID: 3", lines[18]);
            Assert.Equal("Map Name: Home", lines[19]);
            Assert.Equal("Map Can Quick Filter: true", lines[20]);
            Assert.Equal("Raw Player ID: 9223372036854775807", lines[21]);
            Assert.Equal("Raw Player ID Is Integer: false", lines[22]);
            Assert.Equal("Raw Other ID: -9223372036854775808", lines[23]);
            Assert.Equal("Raw Other ID Is Integer: true", lines[24]);
            Assert.Equal("Raw Map ID: 5", lines[25]);
            Assert.Equal("Raw Map ID Is Integer: true", lines[26]);
            Assert.Equal("Raw Map X: 6", lines[27]);
            Assert.Equal("Raw Map X Is Integer: false", lines[28]);
            Assert.Equal("Raw Map Y: 7", lines[29]);
            Assert.Equal("Raw Map Y Is Integer: true", lines[30]);
            Assert.Equal("Original Text: text", lines[31]);
        }

        [Fact]
        public void FormatIsInvariantUnderNonEnglishCulture()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                string text = LogDetailsFormatter.Format(FullRow());
                Assert.Equal("Row ID: 9223372036854775807", text.Split('\n')[2]);
                Assert.Equal("UTC: 2023-11-14T22:13:20.123Z", text.Split('\n')[1]);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void NullRelatedAndMapProduceEmptyValuesWithIndependentOtherIdKind()
        {
            var row = new LogRow(
                1,
                0L,
                5L,
                true,
                "L",
                "G",
                LogOtherIdKind.NpcTemplate,
                new LogRowEntity("Stored", LogEntityKind.StoredValue, 9L, "S", false),
                null,
                null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true),
                "s",
                "t");
            var lines = LogDetailsFormatter.Format(row).Split('\n');
            Assert.Equal("Other ID Kind: NpcTemplate", lines[7]);
            Assert.Equal("Primary Kind: StoredValue", lines[9]);
            Assert.Equal("Related Label: ", lines[13]);
            Assert.Equal("Related Kind: ", lines[14]);
            Assert.Equal("Related ID: ", lines[15]);
            Assert.Equal("Related Name: ", lines[16]);
            Assert.Equal("Related Can Quick Filter: ", lines[17]);
            Assert.Equal("Map ID: ", lines[18]);
            Assert.Equal("Map Name: ", lines[19]);
            Assert.Equal("Map Can Quick Filter: ", lines[20]);
        }

        [Fact]
        public void OriginalTextIsPreservedVerbatimWithoutEscapingOrOmission()
        {
            string original = "line1\r\nline2\x00nul\x1sep|pipe,comma café  spaces";
            var row = new LogRow(
                1, 0L, 5L, true, "L", "G", LogOtherIdKind.Unused,
                new LogRowEntity("P", LogEntityKind.Player, 1L, "N", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true),
                "s", original);
            string text = LogDetailsFormatter.Format(row);
            Assert.EndsWith("Original Text: " + original, text);
            Assert.Equal(original, text.Substring(text.IndexOf("Original Text: ", StringComparison.Ordinal) + "Original Text: ".Length));

            var unicode = new LogRow(
                1, 0L, 5L, true, "Lé", "G", LogOtherIdKind.Unused,
                new LogRowEntity("P", LogEntityKind.Player, 1L, "Némo", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true),
                "résumé,|", original);
            string unicodeText = LogDetailsFormatter.Format(unicode);
            Assert.Contains("Type Label: Lé", unicodeText);
            Assert.Contains("Primary Name: Némo", unicodeText);
            Assert.Contains("Summary: résumé,|", unicodeText);
        }

        [Fact]
        public void TableProjectionIsExactlySixServerOrderColumns()
        {
            var row = FullRow();
            var columns = LogDetailsFormatter.TableColumns(row);
            Assert.Equal(6, columns.Length);
            Assert.Equal("2023-11-14 22:13:20.123", columns[0]);
            Assert.Equal("Ban", columns[1]);
            Assert.Equal("Player", columns[2]);
            Assert.Equal("Guild", columns[3]);
            Assert.Equal("Home", columns[4]);
            Assert.Equal("sum", columns[5]);

            var sparse = new LogRow(
                1, 0L, 5L, true, "L", "G", LogOtherIdKind.Unused,
                new LogRowEntity("P", LogEntityKind.Player, 1L, "N", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true),
                "s", "t");
            var sparseColumns = LogDetailsFormatter.TableColumns(sparse);
            Assert.Equal("1970-01-01 00:00:00.000", sparseColumns[0]);
            Assert.Equal("L", sparseColumns[1]);
            Assert.Equal("P", sparseColumns[2]);
            Assert.Equal("", sparseColumns[3]);
            Assert.Equal("", sparseColumns[4]);
            Assert.Equal("s", sparseColumns[5]);
        }

        [Fact]
        public void OutOfRangeUtcMillisecondsRenderAsRawDecimalWithoutThrowing()
        {
            long min = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();
            long max = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();
            foreach (long value in new[] { long.MinValue, long.MaxValue, min - 1, max + 1 })
            {
                var row = new LogRow(
                    1, value, 5L, true, "L", "G", LogOtherIdKind.Unused,
                    new LogRowEntity("P", LogEntityKind.Player, 1L, "N", true), null, null,
                    new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
                string raw = value.ToString(CultureInfo.InvariantCulture);
                Assert.Equal(raw, LogDetailsFormatter.TableColumns(row)[0]);
                Assert.Equal("UTC: " + raw, LogDetailsFormatter.Format(row).Split('\n')[1]);
                Assert.Equal("UTC: " + raw, LogDetailsFormatter.FormatClipboard(row).Split('\n')[1]);
            }
            Assert.Equal("2023-11-14 22:13:20.123", LogDetailsFormatter.TableColumns(FullRow())[0]);
            Assert.Equal("UTC: 2023-11-14T22:13:20.123Z", LogDetailsFormatter.Format(FullRow()).Split('\n')[1]);
        }

        [Fact]
        public void SupportedUtcBoundariesRenderInIsoFormat()
        {
            long min = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();
            long max = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();
            Assert.Equal(-62135596800000L, min);
            Assert.Equal(253402300799999L, max);
            foreach (var (value, table, iso) in new[]
            {
                (min, "0001-01-01 00:00:00.000", "0001-01-01T00:00:00.000Z"),
                (max, "9999-12-31 23:59:59.999", "9999-12-31T23:59:59.999Z")
            })
            {
                var row = new LogRow(
                    1, value, 5L, true, "L", "G", LogOtherIdKind.Unused,
                    new LogRowEntity("P", LogEntityKind.Player, 1L, "N", true), null, null,
                    new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
                Assert.Equal(table, LogDetailsFormatter.TableColumns(row)[0]);
                Assert.Equal("UTC: " + iso, LogDetailsFormatter.Format(row).Split('\n')[1]);
            }
        }

        [Fact]
        public void DetailsAndClipboardShareTheFormatterAndDoNotMutate()
        {
            var row = FullRow();
            string before = LogDetailsFormatter.Format(row);
            string details = LogDetailsFormatter.FormatDetails(row);
            string clipboard = LogDetailsFormatter.FormatClipboard(row);
            Assert.Equal(before, details);
            Assert.Equal(before, clipboard);
            Assert.Equal(before, LogDetailsFormatter.Format(row));
        }
    }
}
