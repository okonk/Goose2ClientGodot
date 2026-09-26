using System;
using System.Text;
using Goose2Client.Logs;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests
{
    public class LogRowJsonParserTests
    {
        private static readonly string EmptyJson = new string('"', 2);

        private const string EntityTemplate =
            "{{\"label\":{0},\"kind\":{1},\"id\":{2},\"name\":{3},\"canQuickFilter\":{4}}}";

        private const string RawTemplate =
            "{{\"playerId\":{0},\"playerIdIsInteger\":{1},\"otherId\":{2},\"otherIdIsInteger\":{3}," +
            "\"mapId\":{4},\"mapIdIsInteger\":{5},\"mapX\":{6},\"mapXIsInteger\":{7},\"mapY\":{8},\"mapYIsInteger\":{9}}}";

        private static string Js(string s) => "\"" + s + "\"";

        private static string Row(
            string rowId = "0", string utc = "0", string typeId = "0", string typeIsInteger = "true",
            string eventLabel = null, string eventGroup = null, string otherIdKind = null,
            string primary = null, string related = "null", string map = "null",
            string raw = null, string summary = null, string originalText = null)
        {
            if (eventLabel == null) eventLabel = EmptyJson;
            if (eventGroup == null) eventGroup = EmptyJson;
            if (otherIdKind == null) otherIdKind = Js("Unused");
            if (summary == null) summary = EmptyJson;
            if (originalText == null) originalText = EmptyJson;
            if (primary == null)
                primary = string.Format(EntityTemplate, EmptyJson, Js("Player"), "0", EmptyJson, "false");
            if (raw == null)
                raw = string.Format(RawTemplate, "0", "true", "0", "true", "0", "true", "0", "true", "0", "true");
            return "{\"rowId\":" + rowId + ",\"utcMilliseconds\":" + utc + ",\"typeId\":" + typeId +
                   ",\"typeIsInteger\":" + typeIsInteger + ",\"eventLabel\":" + eventLabel +
                   ",\"eventGroup\":" + eventGroup + ",\"otherIdKind\":" + otherIdKind +
                   ",\"primary\":" + primary + ",\"related\":" + related + ",\"map\":" + map +
                   ",\"raw\":" + raw + ",\"summary\":" + summary + ",\"originalText\":" + originalText + "}";
        }

        private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

        private static LogRow Parse(string json)
        {
            Assert.True(LogRowJsonParser.TryParse(Bytes(json), out LogRow row), "expected valid row JSON: " + json);
            return row;
        }

        private static void AssertRejects(string json)
        {
            Assert.False(LogRowJsonParser.TryParse(Bytes(json), out LogRow row));
            Assert.Null(row);
        }

        [Fact]
        public void ParsesCanonicalRow()
        {
            var row = Parse(Row());
            Assert.Equal(0, row.RowId);
            Assert.Equal(0, row.UtcMilliseconds);
            Assert.Equal(0, row.TypeId);
            Assert.True(row.TypeIsInteger);
            Assert.Equal("", row.EventLabel);
            Assert.Equal("", row.EventGroup);
            Assert.Equal(LogOtherIdKind.Unused, row.OtherIdKind);
            Assert.NotNull(row.Primary);
            Assert.Equal(LogEntityKind.Player, row.Primary.Kind);
            Assert.Equal(0, row.Primary.Id);
            Assert.False(row.Primary.CanQuickFilter);
            Assert.Null(row.Related);
            Assert.Null(row.Map);
            Assert.True(row.Raw.PlayerIdIsInteger);
            Assert.Equal(0, row.Raw.MapY);
            Assert.Equal("", row.Summary);
            Assert.Equal("", row.OriginalText);
        }

        [Fact]
        public void RequiresExactPropertyOrder()
        {
            string good = Row(rowId: "1", utc: "2");
            int cut = good.IndexOf(",\"typeId\"", StringComparison.Ordinal);
            string rest = good[cut..];
            string swapped = "{\"utcMilliseconds\":2,\"rowId\":1" + rest;
            AssertRejects(swapped);
        }

        [Fact]
        public void RejectsDuplicateProperties()
        {
            string good = Row();
            int afterFirst = good.IndexOf(",\"utcMilliseconds\"", StringComparison.Ordinal);
            string duplicate = good.Insert(afterFirst, ",\"rowId\":0");
            AssertRejects(duplicate);
        }

        [Fact]
        public void RejectsUnknownProperties()
        {
            string good = Row();
            string unknown = good[..^1] + ",\"unknown\":1}";
            AssertRejects(unknown);
        }

        [Fact]
        public void RejectsTrailingJson()
        {
            string good = Row();
            AssertRejects(good + "{}");
            AssertRejects(good + "null");
            AssertRejects(good + ",");
        }

        [Fact]
        public void RequiresObjectRoot()
        {
            AssertRejects("[]");
            AssertRejects("null");
            AssertRejects("42");
            AssertRejects("");
        }

        [Fact]
        public void PreservesInt64ExtremesWithoutNarrowing()
        {
            string raw = string.Format(RawTemplate,
                "-9223372036854775808", "true", "9223372036854775807", "false",
                "-1", "true", "9223372036854775807", "true", "-9223372036854775808", "false");
            var row = Parse(Row(rowId: "-9223372036854775808", utc: "-1", typeId: "9223372036854775807", raw: raw));
            Assert.Equal(long.MinValue, row.RowId);
            Assert.Equal(-1, row.UtcMilliseconds);
            Assert.Equal(long.MaxValue, row.TypeId);
            Assert.Equal(long.MinValue, row.Raw.PlayerId);
            Assert.True(row.Raw.PlayerIdIsInteger);
            Assert.Equal(long.MaxValue, row.Raw.OtherId);
            Assert.Equal(-1, row.Raw.MapId);
            Assert.Equal(long.MaxValue, row.Raw.MapX);
            Assert.Equal(long.MinValue, row.Raw.MapY);
            Assert.False(row.Raw.MapYIsInteger);
        }

        [Fact]
        public void PreservesValidityFlagsAndKinds()
        {
            string raw = string.Format(RawTemplate, "1", "false", "2", "false", "3", "false", "4", "false", "5", "false");
            string primary = string.Format(EntityTemplate, Js("P"), Js("StoredValue"), "null", Js("N"), "true");
            string related = string.Format(EntityTemplate, Js("R"), Js("Guild"), "7", Js("RN"), "false");
            string map = "{" + Js("id") + ":42," + Js("name") + ":" + Js("Map1") + "," + Js("canQuickFilter") + ":true}";
            var row = Parse(Row(typeIsInteger: "false", otherIdKind: Js("NpcTemplate"), primary: primary, related: related, map: map, raw: raw));
            Assert.False(row.TypeIsInteger);
            Assert.Equal(LogOtherIdKind.NpcTemplate, row.OtherIdKind);
            Assert.Equal(LogEntityKind.StoredValue, row.Primary.Kind);
            Assert.Null(row.Primary.Id);
            Assert.True(row.Primary.CanQuickFilter);
            Assert.Equal("P", row.Primary.Label);
            Assert.Equal("N", row.Primary.Name);
            Assert.NotNull(row.Related);
            Assert.Equal(LogEntityKind.Guild, row.Related.Kind);
            Assert.Equal(7, row.Related.Id);
            Assert.NotNull(row.Map);
            Assert.Equal(42, row.Map.Id);
            Assert.Equal("Map1", row.Map.Name);
            Assert.True(row.Map.CanQuickFilter);
            Assert.Equal(1, row.Raw.PlayerId);
            Assert.False(row.Raw.PlayerIdIsInteger);
            Assert.Equal(2, row.Raw.OtherId);
            Assert.Equal(3, row.Raw.MapId);
            Assert.Equal(4, row.Raw.MapX);
            Assert.Equal(5, row.Raw.MapY);
        }

        [Theory]
        [InlineData("Unused")]
        [InlineData("Player")]
        [InlineData("Guild")]
        [InlineData("Item")]
        [InlineData("NpcTemplate")]
        public void OtherIdKind_AcceptsStableNames(string name)
        {
            var row = Parse(Row(otherIdKind: Js(name)));
            Assert.Equal(name, row.OtherIdKind.ToString());
        }

        [Theory]
        [InlineData("Player")]
        [InlineData("Item")]
        [InlineData("Guild")]
        [InlineData("NpcTemplate")]
        [InlineData("Map")]
        [InlineData("StoredValue")]
        public void EntityKind_AcceptsStableNames(string name)
        {
            string primary = string.Format(EntityTemplate, EmptyJson, Js(name), "0", EmptyJson, "false");
            var row = Parse(Row(primary: primary));
            Assert.Equal(name, row.Primary.Kind.ToString());
        }

        [Fact]
        public void RelatedAndMap_AreIndependentlyNullable()
        {
            string map = "{" + Js("id") + ":1," + Js("name") + ":" + Js("M") + "," + Js("canQuickFilter") + ":false}";
            var row = Parse(Row(map: map));
            Assert.Null(row.Related);
            Assert.NotNull(row.Map);

            string related = string.Format(EntityTemplate, Js("R"), Js("Item"), "null", Js("RN"), "true");
            var row2 = Parse(Row(related: related));
            Assert.NotNull(row2.Related);
            Assert.Null(row2.Map);
            Assert.Null(row2.Related.Id);
        }

        [Fact]
        public void PreservesUnicodeAndEscapesInAllTextLocations()
        {
            char bs = (char)0x5C;
            string text = "héllo,日本語" + char.ConvertFromUtf32(0x1F986) + " a,b|c" + (char)0x01 + (char)0x00 + (char)0x0D + (char)0x0A;
            var sb = new StringBuilder("\"");
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append(bs).Append('"'); break;
                    case (char)0x5C: sb.Append(bs).Append(bs); break;
                    case (char)0x0A: sb.Append(bs).Append("n"); break;
                    case (char)0x0D: sb.Append(bs).Append("r"); break;
                    default:
                        if (c < 0x20)
                            sb.Append(bs).Append("u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            string jsonText = sb.ToString();
            string primary = string.Format(EntityTemplate, jsonText, Js("Player"), "0", jsonText, "false");
            var row = Parse(Row(eventLabel: jsonText, eventGroup: jsonText, primary: primary, summary: jsonText, originalText: jsonText));
            Assert.Equal(text, row.EventLabel);
            Assert.Equal(text, row.EventGroup);
            Assert.Equal(text, row.Primary.Label);
            Assert.Equal(text, row.Primary.Name);
            Assert.Equal(text, row.Summary);
            Assert.Equal(text, row.OriginalText);
        }

        [Fact]
        public void AcceptsExactlyMaxDecodedBytes()
        {
            string skeleton = Row(originalText: Js("__PAD__"));
            int skeletonLen = Encoding.UTF8.GetByteCount(skeleton);
            int padLen = LogRowJsonParser.MaxDecodedBytes - skeletonLen + 7;
            string json = Row(originalText: Js(new string('a', padLen)));
            Assert.Equal(LogRowJsonParser.MaxDecodedBytes, Encoding.UTF8.GetByteCount(json));
            Assert.True(LogRowJsonParser.TryParse(Bytes(json), out LogRow row));
            Assert.Equal(new string('a', padLen), row.OriginalText);
        }

        [Fact]
        public void RejectsOneByteOverLimit()
        {
            string skeleton = Row(originalText: Js("__PAD__"));
            int skeletonLen = Encoding.UTF8.GetByteCount(skeleton);
            int padLen = LogRowJsonParser.MaxDecodedBytes - skeletonLen + 7 + 1;
            string json = Row(originalText: Js(new string('a', padLen)));
            Assert.Equal(LogRowJsonParser.MaxDecodedBytes + 1, Encoding.UTF8.GetByteCount(json));
            AssertRejects(json);
        }

        [Fact]
        public void RejectsInvalidUtf8()
        {
            byte[] bytes = Bytes(Row(originalText: Js("é")));
            byte[] truncated = new byte[bytes.Length - 1];
            Array.Copy(bytes, truncated, truncated.Length);
            Assert.False(LogRowJsonParser.TryParse(truncated, out _));
        }

        [Fact]
        public void RejectsMalformedJson()
        {
            AssertRejects("{");
            AssertRejects("{\"rowId\":0");
            AssertRejects("{\"rowId\":0}");
        }

        [Fact]
        public void RejectsWrongPrimitiveTypes()
        {
            AssertRejects(Row(rowId: Js("0")));
            AssertRejects(Row(utc: Js("0")));
            AssertRejects(Row(typeIsInteger: "0"));
            AssertRejects(Row(eventLabel: "0"));
            AssertRejects(Row(otherIdKind: "0"));
            string primary = string.Format(EntityTemplate, "0", Js("Player"), "0", EmptyJson, "false");
            AssertRejects(Row(primary: primary));
            primary = string.Format(EntityTemplate, EmptyJson, Js("Player"), Js("5"), EmptyJson, "false");
            AssertRejects(Row(primary: primary));
            primary = string.Format(EntityTemplate, EmptyJson, Js("Player"), "0", EmptyJson, Js("true"));
            AssertRejects(Row(primary: primary));
            string raw = string.Format(RawTemplate, "0", "1", "0", "true", "0", "true", "0", "true", "0", "true");
            AssertRejects(Row(raw: raw));
        }

        [Fact]
        public void RejectsInvalidEnumNames()
        {
            AssertRejects(Row(otherIdKind: Js("Foo")));
            AssertRejects(Row(otherIdKind: Js("player")));
            string primary = string.Format(EntityTemplate, EmptyJson, Js("StoredValueX"), "0", EmptyJson, "false");
            AssertRejects(Row(primary: primary));
            primary = string.Format(EntityTemplate, EmptyJson, Js("player"), "0", EmptyJson, "false");
            AssertRejects(Row(primary: primary));
        }

        [Fact]
        public void RejectsMissingProperties()
        {
            string good = Row();
            int idx = good.IndexOf(",\"originalText\"", StringComparison.Ordinal);
            AssertRejects(good[..idx] + "}");

            int idx2 = good.IndexOf(",\"summary\"", StringComparison.Ordinal);
            string missingSummary = good.Remove(idx2, good.IndexOf(",\"originalText\"", idx2) - idx2);
            AssertRejects(missingSummary);
        }
    }
}
