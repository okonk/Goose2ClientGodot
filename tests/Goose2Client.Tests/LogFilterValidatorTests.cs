using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Goose2Client.Logs;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Tests
{
    public class LogFilterValidatorTests
    {
        private static readonly DateTime Clock = new(2026, 9, 26, 12, 34, 56, 789, DateTimeKind.Utc);
        private const long ClockMs = 1790426096789L;

        private static LogTypeMetadata Lmt(int typeId, string group, string label)
            => LogPacketParsing.ParseLmt($"LMT5,{typeId},{Convert.ToBase64String(Encoding.UTF8.GetBytes(group))},{Convert.ToBase64String(Encoding.UTF8.GetBytes(label))}");

        private static LogMapMetadata Lmm(int mapId, string name)
            => LogPacketParsing.ParseLmm($"LMM5,{mapId},{Convert.ToBase64String(Encoding.UTF8.GetBytes(name))}");

        private static LogDefaultsMetadata Lmd(long start, long end)
            => LogPacketParsing.ParseLmd($"LMD5,{start},{end}");

        private static LogMetadataState MetadataWithDefaults()
        {
            var m = new LogMetadataState(5);
            m.FeedLmt(Lmt(12, "Communication", "Chat"));
            m.FeedLmt(Lmt(7, "GM Actions", "Ban"));
            m.FeedLmm(Lmm(3, "Home"));
            m.FeedLmm(Lmm(9, "Docks"));
            m.FeedLmd(Lmd(111, 222));
            return m;
        }

        [Fact]
        public void Metadata_CollectsLmtLmmInArrivalOrder()
        {
            var m = MetadataWithDefaults();
            Assert.Equal(new[] { 12, 7 }, m.Types.Select(t => t.TypeId).ToArray());
            Assert.Equal(new[] { "Communication", "GM Actions" }, m.Types.Select(t => t.Group).ToArray());
            Assert.Equal(new[] { "Chat", "Ban" }, m.Types.Select(t => t.Label).ToArray());
            Assert.Equal(new[] { 3, 9 }, m.Maps.Select(t => t.MapId).ToArray());
            Assert.Equal(new[] { "Home", "Docks" }, m.Maps.Select(t => t.MapName).ToArray());
        }

        [Fact]
        public void Metadata_RejectsUnknownGroupNames()
        {
            var m = new LogMetadataState(5);
            Assert.False(m.FeedLmt(Lmt(1, "Bogus", "X")));
            Assert.True(m.Malformed);
            Assert.Empty(m.Types);
            foreach (string group in new[] { "Communication", "Sessions/Security", "Social", "Items/Economy", "GM Actions", "Other/Retired" })
            {
                var ok = new LogMetadataState(5);
                Assert.True(ok.FeedLmt(Lmt(1, group, "X")));
                Assert.False(ok.Malformed);
            }
        }

        [Fact]
        public void Metadata_DuplicateIdMustMatchConflictMarksStickyMalformed()
        {
            var m = new LogMetadataState(5);
            Assert.True(m.FeedLmt(Lmt(12, "Communication", "Chat")));
            Assert.False(m.FeedLmt(Lmt(12, "GM Actions", "Chat")));
            Assert.True(m.Malformed);
            Assert.False(m.FeedLmt(Lmt(13, "Social", "Y")));
            Assert.Empty(m.Types);
            var maps = new LogMetadataState(5);
            Assert.True(maps.FeedLmm(Lmm(3, "Home")));
            Assert.False(maps.FeedLmm(Lmm(3, "Dock")));
            Assert.True(maps.Malformed);
            Assert.False(maps.FeedLmm(Lmm(4, "Dock")));
            Assert.Empty(maps.Maps);
        }

        [Fact]
        public void Metadata_StickyMalformedMetadataBlocksCompletion()
        {
            var m = new LogMetadataState(5);
            Assert.False(m.FeedLmt(LogPacketParsing.ParseLmt("LMT5,12,!!!,REVG")));
            Assert.True(m.Malformed);
            Assert.False(m.FeedLmd(Lmd(1, 2)));
            Assert.False(m.Complete);
            Assert.Equal(0, m.DefaultStartUnixMs);
        }

        [Fact]
        public void Metadata_LmdIsTheOnlyCompletionMarker()
        {
            var m = new LogMetadataState(5);
            m.FeedLmt(Lmt(12, "Communication", "Chat"));
            m.FeedLmm(Lmm(3, "Home"));
            Assert.False(m.Complete);
            Assert.True(m.FeedLmd(Lmd(111, 222)));
            Assert.True(m.Complete);
            Assert.Equal(111L, m.DefaultStartUnixMs);
            Assert.Equal(222L, m.DefaultEndUnixMs);
        }

        [Fact]
        public void Metadata_IgnoresOtherWindows()
        {
            var m = new LogMetadataState(5);
            Assert.False(m.FeedLmt(LogPacketParsing.ParseLmt("LMT6,12,QUJD,REVG")));
            Assert.Empty(m.Types);
            Assert.False(m.FeedLmm(LogPacketParsing.ParseLmm("LMM6,3,SG9tZQ==")));
            Assert.Empty(m.Maps);
        }

        [Fact]
        public void Presets_UseExactFixedClockBoundaries()
        {
            var draft = new LogFilterDraft { Preset = LogFilterPreset.Previous24Hours };
            LogFilterValidator.ApplyPreset(draft, Clock);
            Assert.Equal(ClockMs - 24L * 3600_000L, draft.StartUnixMs);
            Assert.Equal(ClockMs, draft.EndUnixMs);
            LogFilterValidator.ApplyPreset(draft, Clock.AddSeconds(10));
            Assert.Equal(ClockMs + 10_000L - 24L * 3600_000L, draft.StartUnixMs);
            Assert.Equal(ClockMs + 10_000L, draft.EndUnixMs);

            draft.Preset = LogFilterPreset.LastHour;
            LogFilterValidator.ApplyPreset(draft, Clock);
            Assert.Equal(ClockMs - 3600_000L, draft.StartUnixMs);
            draft.Preset = LogFilterPreset.Previous7Days;
            LogFilterValidator.ApplyPreset(draft, Clock);
            Assert.Equal(ClockMs - 7L * 86_400_000L, draft.StartUnixMs);
            draft.Preset = LogFilterPreset.Previous30Days;
            LogFilterValidator.ApplyPreset(draft, Clock);
            Assert.Equal(LogFilterPreset.Previous30Days, draft.Preset);
            Assert.Equal(ClockMs - 30L * 86_400_000L, draft.StartUnixMs);
        }

        [Fact]
        public void Custom_RequiresExactUtcFormatAndIncreasingRange()
        {
            var draft = new LogFilterDraft
            {
                Preset = LogFilterPreset.Custom,
                StartText = "2026-09-01 10:00:00",
                EndText = "2026-09-02 10:00:00"
            };
            var result = LogFilterValidator.Validate(draft, MetadataWithDefaults());
            Assert.True(result.Success, result.Error);
            Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), result.Snapshot.StartUnixMs);
            Assert.Equal(new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), result.Snapshot.EndUnixMs);

            draft.StartText = "2026-09-01T10:00:00";
            Assert.False(LogFilterValidator.Validate(draft, MetadataWithDefaults()).Success);
            draft.StartText = "2026-09-01 10:00:00";
            draft.EndText = "2026-09-01 10:00:00";
            Assert.False(LogFilterValidator.Validate(draft, MetadataWithDefaults()).Success);
            draft.EndText = "2026-09-01 09:59:59";
            Assert.False(LogFilterValidator.Validate(draft, MetadataWithDefaults()).Success);
            draft.StartText = "";
            Assert.False(LogFilterValidator.Validate(draft, MetadataWithDefaults()).Success);
        }

        [Fact]
        public void Custom_EnforcesThirtyOneDayAndSevenDayTextBoundaries()
        {
            var ok = new LogFilterDraft
            {
                Preset = LogFilterPreset.Custom,
                StartText = "2026-09-01 00:00:00",
                EndText = "2026-10-02 00:00:00"
            };
            Assert.True(LogFilterValidator.Validate(ok, MetadataWithDefaults()).Success);

            var over = new LogFilterDraft
            {
                Preset = LogFilterPreset.Custom,
                StartText = "2026-09-01 00:00:00",
                EndText = "2026-10-02 00:00:01"
            };
            Assert.False(LogFilterValidator.Validate(over, MetadataWithDefaults()).Success);

            var textOk = new LogFilterDraft
            {
                Preset = LogFilterPreset.Custom,
                StartText = "2026-09-01 00:00:00",
                EndText = "2026-09-08 00:00:00",
                Text = "fire"
            };
            Assert.True(LogFilterValidator.Validate(textOk, MetadataWithDefaults()).Success);

            var textOver = new LogFilterDraft
            {
                Preset = LogFilterPreset.Custom,
                StartText = "2026-09-01 00:00:00",
                EndText = "2026-09-08 00:00:01",
                Text = "fire"
            };
            Assert.False(LogFilterValidator.Validate(textOver, MetadataWithDefaults()).Success);

            var emptyTextWide = new LogFilterDraft
            {
                Preset = LogFilterPreset.Custom,
                StartText = "2026-09-01 00:00:00",
                EndText = "2026-10-02 00:00:00",
                Text = ""
            };
            Assert.True(LogFilterValidator.Validate(emptyTextWide, MetadataWithDefaults()).Success);
        }

        private static LogFilterDraft Draft()
        {
            var d = new LogFilterDraft();
            LogFilterValidator.ApplyPreset(d, Clock);
            return d;
        }

        [Fact]
        public void Participant_AcceptsEmptyNameAndPositiveIdOnly()
        {
            var meta = MetadataWithDefaults();
            foreach (string valid in new[] { "", "Z", "Player", "#1", "#2147483647" })
            {
                var draft = Draft();
                draft.Participant = valid;
                Assert.True(LogFilterValidator.Validate(draft, meta).Success, valid);
            }
            foreach (string invalid in new[] { "#0", "#-1", "#2147483648", "#1x", "#", "  ", "a b c", "# 1" })
            {
                var draft = Draft();
                draft.Participant = invalid;
                Assert.False(LogFilterValidator.Validate(draft, meta).Success, invalid);
            }
            var over = Draft();
            over.Participant = new string('é', 33);
            Assert.False(LogFilterValidator.Validate(over, meta).Success);
            var boundary = Draft();
            boundary.Participant = new string('é', 32);
            Assert.True(LogFilterValidator.Validate(boundary, meta).Success);
        }

        [Fact]
        public void Map_AcceptsAllSelectedAndRawIdRejectsUnselectedPartial()
        {
            var meta = MetadataWithDefaults();
            foreach (string valid in new[] { "", "Home (#3)", "#3", "#999" })
            {
                var draft = Draft();
                draft.MapText = valid;
                Assert.True(LogFilterValidator.Validate(draft, meta).Success, valid);
            }
            foreach (string invalid in new[] { "Home", "Dock (#9)", "#0", "#-4", "#2147483648", "H" })
            {
                var draft = Draft();
                draft.MapText = invalid;
                Assert.False(LogFilterValidator.Validate(draft, meta).Success, invalid);
            }
        }

        [Fact]
        public void MapSuggestions_AreCaseInsensitivePrefixMatches()
        {
            var meta = MetadataWithDefaults();
            Assert.Equal(new[] { "Home (#3)", "Docks (#9)" }, LogFilterValidator.MapSuggestions(meta, "").ToArray());
            Assert.Equal(new[] { "Docks (#9)" }, LogFilterValidator.MapSuggestions(meta, "d").ToArray());
            Assert.Equal(new[] { "Home (#3)" }, LogFilterValidator.MapSuggestions(meta, "HOME").ToArray());
            Assert.Empty(LogFilterValidator.MapSuggestions(meta, "zz"));
        }

        [Fact]
        public void Types_EmptyMeansAllExplicitSelectionsStaySortedDistinct()
        {
            var meta = MetadataWithDefaults();
            var empty = Draft();
            empty.SelectedTypeIds = new List<int>();
            var result = LogFilterValidator.Validate(empty, meta);
            Assert.True(result.Success);
            Assert.Empty(result.Snapshot.TypeIds);

            var explicitAll = Draft();
            explicitAll.SelectedTypeIds = new List<int> { 12, 7, 12 };
            var all = LogFilterValidator.Validate(explicitAll, meta);
            Assert.True(all.Success);
            Assert.Equal(new[] { 7, 12 }, all.Snapshot.TypeIds.ToArray());
            Assert.NotEqual(0, all.Snapshot.TypeIds.Count);

            var unknown = Draft();
            unknown.SelectedTypeIds = new List<int> { 999 };
            Assert.False(LogFilterValidator.Validate(unknown, meta).Success);
        }

        [Fact]
        public void Text_PreservesLiteralContent()
        {
            var meta = MetadataWithDefaults();
            string literal = "  lead % _ \\ , \u0001\u000b café  ";
            var draft = Draft();
            draft.Text = literal;
            var result = LogFilterValidator.Validate(draft, meta);
            Assert.True(result.Success);
            Assert.Equal(literal, result.Snapshot.Text);
            var tooLong = Draft();
            tooLong.Text = new string('x', 4097);
            Assert.False(LogFilterValidator.Validate(tooLong, meta).Success);
            var boundary = Draft();
            boundary.Text = new string('x', 4096);
            Assert.True(LogFilterValidator.Validate(boundary, meta).Success);
        }

        [Fact]
        public void Clear_MutatesDraftsOnlyWithOneNewUtcInstant()
        {
            var draft = new LogFilterDraft
            {
                Preset = LogFilterPreset.Previous7Days,
                StartUnixMs = 1,
                EndUnixMs = 2,
                Participant = "Player",
                MapText = "Home (#3)",
                SelectedTypeIds = new List<int> { 7, 12 },
                Text = "fire"
            };
            LogFilterValidator.Clear(draft, Clock);
            Assert.Equal(LogFilterPreset.Previous24Hours, draft.Preset);
            Assert.Equal(ClockMs, draft.EndUnixMs);
            Assert.Equal(ClockMs - 24L * 3600_000L, draft.StartUnixMs);
            Assert.Equal("", draft.Participant);
            Assert.Equal("", draft.MapText);
            Assert.Empty(draft.SelectedTypeIds);
            Assert.Equal("", draft.Text);
            LogFilterValidator.Clear(draft, Clock.AddSeconds(5));
            Assert.Equal(ClockMs + 5_000L, draft.EndUnixMs);
        }
    }
}
