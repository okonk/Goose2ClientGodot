using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Goose2Client.Logs;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Tests
{
    public class LogViewerStateTests
    {
        private const int Window = 5;
        private const string Token1 = "AAECAwQFBgcICQoLDA0ODw";
        private const string Token2 = "BBECAwQFBgcICQoLDA0ODw";
        private const string Token3 = "CCECAwQFBgcICQoLDA0ODw";
        private const string Token4 = "DDECAwQFBgcICQoLDA0ODw";
        private static readonly DateTime Clock = new(2026, 9, 26, 12, 0, 0, 0, DateTimeKind.Utc);
        private const long ClockMs = 1790424000000L;

        private static LogViewerState Open()
        {
            var s = new LogViewerState(Clock);
            s.OnWindowReplacement(Window);
            s.FeedLmt(LogPacketParsing.ParseLmt($"LMT{Window},12,Q29tbXVuaWNhdGlvbg==,Q2hhdA=="));
            s.FeedLmt(LogPacketParsing.ParseLmt($"LMT{Window},7,R00gQWN0aW9ucw==,RmlyZQ=="));
            s.FeedLmm(LogPacketParsing.ParseLmm($"LMM{Window},3,SG9tZQ=="));
            s.FeedLmd(LogPacketParsing.ParseLmd($"LMD{Window},111,222"));
            LogFilterValidator.ApplyPreset(s.Draft, Clock);
            return s;
        }

        private static LogResultBegin Lrb(int request = 1) => LogPacketParsing.ParseLrb($"LRB{Window},{request}");
        private static LogResultData Lrd(int request, int row, int index, int count, string segment) => LogPacketParsing.ParseLrd($"LRD{Window},{request},{row},{index},{count},{segment}");
        private static LogResultFinish Lrf(int request, bool more, string current, string next) => LogPacketParsing.ParseLrf($"LRF{Window},{request},{(more ? "1" : "0")},{current},{next}");
        private static LogResultError Lrx(int request, string message) => LogPacketParsing.ParseLrx($"LRX{Window},{request},{Convert.ToBase64String(Encoding.UTF8.GetBytes(message))}");

        private static string MinimalRow(long rowId)
        {
            return "{\"rowId\":" + rowId +
                ",\"utcMilliseconds\":1700000000000," +
                "\"typeId\":12,\"typeIsInteger\":true," +
                "\"eventLabel\":\"Chat\",\"eventGroup\":\"Communication\"," +
                "\"otherIdKind\":\"Player\"," +
                "\"primary\":{\"label\":\"Player\",\"kind\":\"Player\",\"id\":1,\"name\":\"A\",\"canQuickFilter\":true}," +
                "\"related\":null,\"map\":null," +
                "\"raw\":{\"playerId\":1,\"playerIdIsInteger\":true,\"otherId\":2,\"otherIdIsInteger\":true,\"mapId\":3,\"mapIdIsInteger\":true,\"mapX\":4,\"mapXIsInteger\":true,\"mapY\":5,\"mapYIsInteger\":true}," +
                "\"summary\":\"s\"," +
                "\"originalText\":\"t\"}";
        }

        private static string B64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        private static void DeliverFreshPage(LogViewerState s, int request, string json, string token, string next = "", bool more = false)
        {
            string b64 = B64(json);
            s.FeedLrb(Lrb(request));
            s.FeedLrd(Lrd(request, 0, 0, 1, b64));
            s.FeedLrf(Lrf(request, more, token, next));
        }

        private static LogRow SingleRow(long rowId = 1)
        {
            return new LogRow(
                rowId, 1700000000000L, 12L, true, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Player", LogEntityKind.Player, 1L, "A", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
        }

        [Fact]
        public void NewOpenStartsAtRequestIdOneWithSearchDisabledUntilLmd()
        {
            var s = new LogViewerState(Clock);
            s.OnWindowReplacement(Window);
            Assert.Equal(1, s.NextRequestId);
            Assert.False(s.IsReady);
            Assert.Equal("Waiting for log metadata…", s.StatusText);
            Assert.Null(s.Search());
            s.FeedLmt(LogPacketParsing.ParseLmt($"LMT{Window},12,Q29tbXVuaWNhdGlvbg==,Q2hhdA=="));
            Assert.Null(s.Search());
            s.FeedLmd(LogPacketParsing.ParseLmd($"LMD{Window},111,222"));
            Assert.True(s.IsReady);
            Assert.Equal("Ready", s.StatusText);
            LogFilterValidator.ApplyPreset(s.Draft, Clock);
            Assert.NotNull(s.Search());

            var replaced = new LogViewerState(Clock);
            replaced.OnWindowReplacement(9);
            Assert.Equal(1, replaced.NextRequestId);
            Assert.False(replaced.IsReady);
        }

        [Fact]
        public void FreshSnapshotsDraftsHasNoTokenAndStaysLoadingWithoutClearingRows()
        {
            var s = Open();
            var submission = s.Search();
            Assert.NotNull(submission);
            var fresh = Assert.IsType<LogQuerySubmission.Fresh>(submission);
            Assert.Equal(1, fresh.RequestId);
            Assert.Equal(ClockMs - 24L * 3600_000L, fresh.Filter.StartUnixMs);
            Assert.Equal(ClockMs, fresh.Filter.EndUnixMs);
            Assert.Empty(fresh.Filter.TypeIds);
            Assert.True(s.IsActive);
            Assert.Equal("Loading…", s.StatusText);
            Assert.Null(s.Search());
            Assert.Null(s.Next());
            Assert.Null(s.Previous());

            var previous = Open();
            previous.Search();
            DeliverFreshPage(previous, 1, MinimalRow(99), Token1);
            Assert.Single(previous.Rows);
            var second = previous.Search();
            Assert.NotNull(second);
            Assert.Single(previous.Rows);
            Assert.Equal("Loading…", previous.StatusText);
        }

        [Fact]
        public void FirstFreshHistoryIsExactlyTheIssuedNonemptyToken()
        {
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1);
            Assert.Equal(new[] { Token1 }, s.History.Select(t => t).ToArray());
            Assert.Equal(0, s.HistoryIndex);
            Assert.Equal(Token1, s.CurrentToken);
            Assert.False(s.History.Contains(""));
            Assert.DoesNotContain("", s.History);
        }

        [Fact]
        public void PreviousAndNextProducePageSubmissionsWithOnlyTheirTargetToken()
        {
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1, Token2, more: true);

            s.Draft.Participant = "Edited";
            s.Draft.Text = "edited";
            var next = Assert.IsType<LogQuerySubmission.Page>(s.Next());
            Assert.Equal(2, next.RequestId);
            Assert.Equal(Token2, next.PageToken);
            Assert.Equal(LogNavigationIntent.Next, next.Intent);
            Assert.True(s.IsActive);
            Assert.Null(s.Next());
            Assert.Null(s.Previous());

            s.FeedLrb(Lrb(2));
            s.FeedLrd(Lrd(2, 0, 0, 1, B64(MinimalRow(2))));
            s.FeedLrf(Lrf(2, true, Token2, Token3));
            Assert.Equal(new[] { Token1, Token2 }, s.History.ToArray());
            Assert.Equal(1, s.HistoryIndex);
            Assert.Equal(Token3, s.NextToken);

            var previous = Assert.IsType<LogQuerySubmission.Page>(s.Previous());
            Assert.Equal(3, previous.RequestId);
            Assert.Equal(Token1, previous.PageToken);
            Assert.Equal(LogNavigationIntent.Previous, previous.Intent);

            Assert.Null(s.Previous());
        }

        [Fact]
        public void PageFinishCommitsOnlyOnExactSubmittedTokenEquality()
        {
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1, Token2, more: true);
            s.Next();
            s.FeedLrb(Lrb(2));
            s.FeedLrd(Lrd(2, 0, 0, 1, B64(MinimalRow(2))));
            s.FeedLrf(Lrf(2, false, Token3, ""));
            Assert.Single(s.Rows);
            Assert.Equal(0, s.HistoryIndex);
            Assert.Equal(new[] { Token1 }, s.History.ToArray());
            Assert.Equal("Protocol failure.", s.StatusText);
            Assert.False(s.IsActive);
        }

        [Fact]
        public void FailedPagingLeavesRowsIndexHistoryUntouched()
        {
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1, Token2, more: true);
            s.Next();
            Assert.False(s.FeedLrd(Lrd(2, 0, 0, 1, B64(MinimalRow(2)))));
            Assert.False(s.FeedLrf(Lrf(2, true, Token2, Token3)));
            Assert.Single(s.Rows);
            Assert.Equal(SingleRow(1), s.Rows[0]);
            Assert.Equal(0, s.HistoryIndex);
            Assert.Equal(new[] { Token1 }, s.History.ToArray());
            Assert.Equal("Protocol failure.", s.StatusText);
        }

        [Fact]
        public void ForwardHistoryIsReusedWhenItAlreadyMatches()
        {
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1, Token2, more: true);
            s.Next();
            s.FeedLrb(Lrb(2));
            s.FeedLrd(Lrd(2, 0, 0, 1, B64(MinimalRow(2))));
            s.FeedLrf(Lrf(2, true, Token2, Token3));
            s.Next();
            s.FeedLrb(Lrb(3));
            s.FeedLrd(Lrd(3, 0, 0, 1, B64(MinimalRow(3))));
            s.FeedLrf(Lrf(3, true, Token3, Token1));
            Assert.Equal(new[] { Token1, Token2, Token3 }, s.History.ToArray());
            Assert.Equal(2, s.HistoryIndex);
        }

        [Fact]
        public void LrbLrdDataRemainInvisibleUntilMatchingLrf()
        {
            var s = Open();
            s.Search();
            s.FeedLrb(Lrb(1));
            s.FeedLrd(Lrd(1, 0, 0, 1, B64(MinimalRow(1))));
            Assert.Empty(s.Rows);
            Assert.Equal("Loading…", s.StatusText);
            s.FeedLrf(Lrf(1, false, Token1, ""));
            Assert.Single(s.Rows);
            Assert.Equal(SingleRow(1), s.Rows[0]);
            Assert.Equal("Showing 1 rows (up to 50), page 1", s.StatusText);
            Assert.False(s.IsActive);
            Assert.Equal("2026-09-25 12:00:00 → 2026-09-26 12:00:00", s.AppliedFilterDescription);
        }

        [Fact]
        public void MalformedMatchingPacketsPreserveOldPageAndShowSafeError()
        {
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1, Token2, more: true);
            Assert.Single(s.Rows);

            var lrx = Open();
            lrx.Search();
            lrx.FeedLrb(Lrb(1));
            lrx.FeedLrx(Lrx(1, "Server busy"));
            Assert.Empty(lrx.Rows);
            Assert.Equal("Server busy", lrx.StatusText);
            Assert.False(lrx.IsActive);
            Assert.False(lrx.FeedLrf(Lrf(1, false, Token1, "")));

            var duplicateBegin = Open();
            duplicateBegin.Search();
            duplicateBegin.FeedLrb(Lrb(1));
            Assert.False(duplicateBegin.FeedLrb(Lrb(1)));
            Assert.Equal("Protocol failure.", duplicateBegin.StatusText);

            var missingChunk = Open();
            missingChunk.Search();
            missingChunk.FeedLrb(Lrb(1));
            missingChunk.FeedLrd(Lrd(1, 0, 0, 2, B64(MinimalRow(1))));
            Assert.False(missingChunk.FeedLrf(Lrf(1, false, Token1, "")));
            Assert.Equal("Protocol failure.", missingChunk.StatusText);

            var afterAbort = Open();
            afterAbort.Search();
            Assert.False(afterAbort.FeedLrd(Lrd(1, 0, 0, 1, B64(MinimalRow(1)))));
            Assert.False(afterAbort.FeedLrf(Lrf(1, false, Token1, "")));
            Assert.Equal("Protocol failure.", afterAbort.StatusText);

            var malformed = Open();
            malformed.Search();
            Assert.False(malformed.FeedLrd(LogPacketParsing.ParseLrd($"LRD{Window},1,0,0,1,QU!D")));
            Assert.Equal("Protocol failure.", malformed.StatusText);
            Assert.False(malformed.FeedLrf(Lrf(1, false, Token1, "")));
        }

        [Fact]
        public void NonmatchingResultIdentityAbortsStageWithoutMutatingVisibleData()
        {
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1);
            s.Search();
            s.FeedLrb(Lrb(2));
            Assert.False(s.FeedLrd(Lrd(3, 0, 0, 1, B64(MinimalRow(2)))));
            Assert.Single(s.Rows);
            Assert.Equal(SingleRow(1), s.Rows[0]);
            Assert.Equal("Protocol failure.", s.StatusText);
            Assert.False(s.IsActive);
            Assert.False(s.FeedLrf(Lrf(2, false, Token2, "")));

            var noStage = Open();
            noStage.Search();
            DeliverFreshPage(noStage, 1, MinimalRow(1), Token1);
            Assert.False(noStage.FeedLrd(Lrd(9, 0, 0, 1, B64(MinimalRow(2)))));
            Assert.Single(noStage.Rows);
            Assert.Equal("Showing 1 rows (up to 50), page 1", noStage.StatusText);
            Assert.False(noStage.IsActive);
        }

        [Fact]
        public void LifecycleBoundariesClearEverything()
        {
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1, Token2, more: true);
            s.Next();
            s.FeedLrb(Lrb(2));
            s.FeedLrd(Lrd(2, 0, 0, 1, B64(MinimalRow(2))));

            s.OnWindowReplacement(9);
            Assert.Empty(s.Rows);
            Assert.Empty(s.History);
            Assert.Equal(-1, s.HistoryIndex);
            Assert.Null(s.CurrentToken);
            Assert.Null(s.NextToken);
            Assert.False(s.IsActive);
            Assert.False(s.IsReady);
            Assert.Equal(1, s.NextRequestId);
            Assert.Equal("Waiting for log metadata…", s.StatusText);
            Assert.Null(s.AppliedFilter);

            s.OnClose();
            Assert.Empty(s.Rows);
            Assert.Equal("Waiting for log metadata…", s.StatusText);
            s.OnDisconnected();
            Assert.Empty(s.Rows);
            s.OnSocketError();
            Assert.Empty(s.Rows);
            s.OnTeardown();
            Assert.Empty(s.Rows);
            Assert.Empty(s.History);
        }

        [Fact]
        public void RequestIdsWrapSafelyAndDoubleSubmissionIsImpossible()
        {
            var s = new LogViewerState(Clock);
            s.OnWindowReplacement(Window);
            s.FeedLmt(LogPacketParsing.ParseLmt($"LMT{Window},12,Q29tbXVuaWNhdGlvbg==,Q2hhdA=="));
            s.FeedLmd(LogPacketParsing.ParseLmd($"LMD{Window},111,222"));
            LogFilterValidator.ApplyPreset(s.Draft, Clock);
            s.NextRequestId = int.MaxValue;
            var a = Assert.IsType<LogQuerySubmission.Fresh>(s.Search());
            Assert.Equal(int.MaxValue, a.RequestId);
            DeliverFreshPage(s, int.MaxValue, MinimalRow(1), Token1);
            var b = Assert.IsType<LogQuerySubmission.Fresh>(s.Search());
            Assert.Equal(1, b.RequestId);
            DeliverFreshPage(s, 1, MinimalRow(2), Token2);
            var c = Assert.IsType<LogQuerySubmission.Fresh>(s.Search());
            Assert.Equal(2, c.RequestId);
            Assert.NotNull(c);
            Assert.Null(s.Search());
        }

        [Fact]
        public void QuickActionsRequireMetadataAndServerEligibilityAndDomain()
        {
            var s = Open();
            var row = SingleRow(1);
            Assert.True(LogViewerState.TypeQuickFilterAvailable(s, row));
            Assert.True(LogViewerState.PrimaryQuickFilterAvailable(s, row));
            Assert.False(LogViewerState.RelatedQuickFilterAvailable(s, row));

            var outOfDomain = new LogRow(
                1, 1700000000000L, 999L, true, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Player", LogEntityKind.Player, 1L, "A", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
            Assert.False(LogViewerState.TypeQuickFilterAvailable(s, outOfDomain));
            Assert.True(LogViewerState.PrimaryQuickFilterAvailable(s, outOfDomain));

            var notInteger = new LogRow(
                1, 1700000000000L, 12L, false, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Player", LogEntityKind.Player, 1L, "A", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
            Assert.False(LogViewerState.TypeQuickFilterAvailable(s, notInteger));

            var noFlag = new LogRow(
                1, 1700000000000L, 12L, true, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Player", LogEntityKind.Player, 1L, "A", false), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
            Assert.False(LogViewerState.PrimaryQuickFilterAvailable(s, noFlag));

            var zeroId = new LogRow(
                1, 1700000000000L, 12L, true, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Player", LogEntityKind.Player, 0L, "A", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
            Assert.False(LogViewerState.PrimaryQuickFilterAvailable(s, zeroId));

            var negativeId = new LogRow(
                1, 1700000000000L, 12L, true, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Player", LogEntityKind.Player, -1L, "A", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
            Assert.False(LogViewerState.PrimaryQuickFilterAvailable(s, negativeId));

            var overflowId = new LogRow(
                1, 1700000000000L, 12L, true, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Player", LogEntityKind.Player, 2147483648L, "A", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
            Assert.False(LogViewerState.PrimaryQuickFilterAvailable(s, overflowId));

            var nullId = new LogRow(
                1, 1700000000000L, 12L, true, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Player", LogEntityKind.Player, null, "A", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
            Assert.False(LogViewerState.PrimaryQuickFilterAvailable(s, nullId));

            var storedValue = new LogRow(
                1, 1700000000000L, 12L, true, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Stored", LogEntityKind.StoredValue, 4L, "V", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
            Assert.False(LogViewerState.PrimaryQuickFilterAvailable(s, storedValue));

            var mapKind = new LogRow(
                1, 1700000000000L, 12L, true, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Map", LogEntityKind.Map, 3L, "Home", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
            Assert.True(LogViewerState.PrimaryQuickFilterAvailable(s, mapKind));

            var related = new LogRow(
                1, 1700000000000L, 12L, true, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Player", LogEntityKind.Player, 1L, "A", true),
                new LogRowEntity("Guild", LogEntityKind.Guild, 8L, "G", true), null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
            Assert.False(LogViewerState.RelatedQuickFilterAvailable(s, related));

            var relatedPlayer = new LogRow(
                1, 1700000000000L, 12L, true, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Player", LogEntityKind.Player, 1L, "A", true),
                new LogRowEntity("Player", LogEntityKind.Player, 8L, "G", true), null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
            Assert.True(LogViewerState.RelatedQuickFilterAvailable(s, relatedPlayer));

            var quick = LogViewerState.GetQuickActions(s, row);
            Assert.True(quick.TypeAvailable);
            Assert.Equal(12, quick.TypeId);
            Assert.True(quick.PrimaryAvailable);
            Assert.Equal(LogEntityKind.Player, quick.PrimaryKind);
            Assert.Equal(1, quick.PrimaryId);
            Assert.False(quick.RelatedAvailable);
            Assert.False(s.Draft.Participant.Equals("#1"));
            s.ApplyQuickAction(LogQuickActionTarget.Primary, row);
            Assert.Equal("#1", s.Draft.Participant);
            s.ApplyQuickAction(LogQuickActionTarget.Type, row);
            Assert.Equal(new[] { 12 }, s.Draft.SelectedTypeIds.ToArray());
            s.ApplyQuickAction(LogQuickActionTarget.Primary, mapKind);
            Assert.Equal("#3", s.Draft.MapText);
            s.ApplyQuickAction(LogQuickActionTarget.Related, relatedPlayer);
            Assert.Equal("#8", s.Draft.Participant);
        }

        [Fact]
        public void PreviousCommitMovesToStoredTokenWithoutMutatingHistory()
        {
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1, Token2, more: true);
            s.Next();
            s.FeedLrb(Lrb(2));
            s.FeedLrd(Lrd(2, 0, 0, 1, B64(MinimalRow(2))));
            s.FeedLrf(Lrf(2, true, Token2, Token3));
            Assert.Equal(new[] { Token1, Token2 }, s.History.ToArray());
            Assert.Equal(1, s.HistoryIndex);

            var previous = Assert.IsType<LogQuerySubmission.Page>(s.Previous());
            Assert.Equal(Token1, previous.PageToken);
            s.FeedLrb(Lrb(previous.RequestId));
            s.FeedLrd(Lrd(previous.RequestId, 0, 0, 1, B64(MinimalRow(9))));
            s.FeedLrf(Lrf(previous.RequestId, true, Token1, Token2));
            Assert.Equal(new[] { Token1, Token2 }, s.History.ToArray());
            Assert.Equal(0, s.HistoryIndex);
            Assert.Equal(SingleRow(9), s.Rows[0]);
            Assert.Equal("Showing 1 rows (up to 50), page 1", s.StatusText);
            Assert.Equal(Token2, s.NextToken);
        }

        [Fact]
        public void NextCommitTruncatesDifferingForwardHistory()
        {
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1, Token2, more: true);
            s.Next();
            s.FeedLrb(Lrb(2));
            s.FeedLrd(Lrd(2, 0, 0, 1, B64(MinimalRow(2))));
            s.FeedLrf(Lrf(2, true, Token2, Token3));
            s.Next();
            s.FeedLrb(Lrb(3));
            s.FeedLrd(Lrd(3, 0, 0, 1, B64(MinimalRow(3))));
            s.FeedLrf(Lrf(3, true, Token3, Token4));
            Assert.Equal(new[] { Token1, Token2, Token3 }, s.History.ToArray());
            Assert.Equal(2, s.HistoryIndex);

            s.Previous();
            s.FeedLrb(Lrb(4));
            s.FeedLrd(Lrd(4, 0, 0, 1, B64(MinimalRow(2))));
            s.FeedLrf(Lrf(4, true, Token2, Token4));
            Assert.Equal(1, s.HistoryIndex);

            var next = Assert.IsType<LogQuerySubmission.Page>(s.Next());
            Assert.Equal(Token4, next.PageToken);
            s.FeedLrb(Lrb(next.RequestId));
            s.FeedLrd(Lrd(next.RequestId, 0, 0, 1, B64(MinimalRow(4))));
            s.FeedLrf(Lrf(next.RequestId, false, Token4, ""));
            Assert.Equal(new[] { Token1, Token2, Token4 }, s.History.ToArray());
            Assert.Equal(2, s.HistoryIndex);
        }

        [Fact]
        public void NextCommitReusesExistingForwardEntryWithoutDuplicate()
        {
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1, Token2, more: true);
            s.Next();
            s.FeedLrb(Lrb(2));
            s.FeedLrd(Lrd(2, 0, 0, 1, B64(MinimalRow(2))));
            s.FeedLrf(Lrf(2, true, Token2, Token3));
            Assert.Equal(new[] { Token1, Token2 }, s.History.ToArray());
            Assert.Equal(1, s.HistoryIndex);

            var previous = Assert.IsType<LogQuerySubmission.Page>(s.Previous());
            Assert.Equal(Token1, previous.PageToken);
            s.FeedLrb(Lrb(previous.RequestId));
            s.FeedLrd(Lrd(previous.RequestId, 0, 0, 1, B64(MinimalRow(1))));
            s.FeedLrf(Lrf(previous.RequestId, true, Token1, Token2));
            Assert.Equal(0, s.HistoryIndex);

            var next = Assert.IsType<LogQuerySubmission.Page>(s.Next());
            Assert.Equal(Token2, next.PageToken);
            s.FeedLrb(Lrb(next.RequestId));
            s.FeedLrd(Lrd(next.RequestId, 0, 0, 1, B64(MinimalRow(2))));
            s.FeedLrf(Lrf(next.RequestId, true, Token2, Token3));
            Assert.Equal(new[] { Token1, Token2 }, s.History.ToArray());
            Assert.Equal(1, s.HistoryIndex);
        }

        [Fact]
        public void NonmatchingLrxMidStageAbortsTerminally()
        {
            var s = Open();
            s.Search();
            s.FeedLrb(Lrb(1));
            s.FeedLrd(Lrd(1, 0, 0, 1, B64(MinimalRow(1))));
            Assert.False(s.FeedLrx(Lrx(9, "stale")));
            Assert.False(s.IsActive);
            Assert.Equal("Protocol failure.", s.StatusText);
            Assert.False(s.FeedLrf(Lrf(1, false, Token1, "")));
            Assert.Empty(s.Rows);
            Assert.Empty(s.History);

            var noStage = Open();
            noStage.Search();
            DeliverFreshPage(noStage, 1, MinimalRow(1), Token1);
            Assert.False(noStage.FeedLrx(Lrx(9, "stale")));
            Assert.Single(noStage.Rows);
            Assert.Equal("Showing 1 rows (up to 50), page 1", noStage.StatusText);
        }

        [Fact]
        public void OversizeResponsePreservesOldPageAndShowsProtocolFailure()
        {
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1);
            Assert.Single(s.Rows);

            s.Search();
            s.FeedLrb(Lrb(2));
            for (int i = 0; i < 340; i++)
                Assert.True(s.FeedLrd(Lrd(2, 0, i, 342, new string('A', 12288))));
            Assert.True(s.FeedLrd(Lrd(2, 0, 340, 342, new string('A', 10349))));
            Assert.False(s.FeedLrd(Lrd(2, 0, 341, 342, "A")));
            Assert.Single(s.Rows);
            Assert.Equal(SingleRow(1), s.Rows[0]);
            Assert.Equal("Protocol failure.", s.StatusText);
            Assert.False(s.IsActive);
            Assert.False(s.FeedLrf(Lrf(2, false, Token2, "")));
            Assert.Equal(new[] { Token1 }, s.History.ToArray());
            Assert.Equal(0, s.HistoryIndex);
        }

        [Fact]
        public void LifecycleResetsClearMetadataAndDrafts()
        {
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1);
            s.Draft.Participant = "Edited";
            s.Draft.Text = "fire";
            Assert.True(s.Metadata.Complete);

            s.OnClose();
            Assert.False(s.Metadata.Complete);
            Assert.Empty(s.Metadata.Types);
            Assert.Empty(s.Metadata.Maps);
            Assert.Equal(LogFilterPreset.Previous24Hours, s.Draft.Preset);
            Assert.Equal("", s.Draft.Participant);
            Assert.Equal("", s.Draft.MapText);
            Assert.Equal("", s.Draft.Text);
            Assert.Empty(s.Draft.SelectedTypeIds);
            Assert.Empty(s.Rows);
            Assert.Empty(s.History);
            Assert.Null(s.AppliedFilter);

            var disconnected = Open();
            disconnected.OnDisconnected();
            Assert.False(disconnected.Metadata.Complete);
            Assert.Equal("", disconnected.Draft.Participant);

            var errored = Open();
            errored.OnSocketError();
            Assert.False(errored.Metadata.Complete);
            Assert.Empty(errored.Metadata.Maps);

            var tornDown = Open();
            tornDown.OnTeardown();
            Assert.False(tornDown.Metadata.Complete);
            Assert.Empty(tornDown.Rows);
            Assert.Empty(tornDown.History);
        }

        [Fact]
        public void DirtyDraftsShowExactDirtyNotice()
        {
            Assert.Equal("Filters edited — Search to apply.", LogViewerState.DirtyNotice);
            var s = Open();
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1);
            Assert.Null(s.DirtyStatusText);
            s.Draft.Text = "edited";
            Assert.Equal("Filters edited — Search to apply.", s.DirtyStatusText);
        }

        [Fact]
        public void StatusTextIsExactForEveryInlineState()
        {
            var s = Open();
            Assert.Equal("Ready", s.StatusText);
            Assert.Equal("Recent entries may be delayed by up to ten minutes.", LogViewerState.FreshnessNotice);

            s.Search();
            Assert.Equal("Loading…", s.StatusText);
            DeliverFreshPage(s, 1, MinimalRow(1), Token1);
            Assert.Equal("Showing 1 rows (up to 50), page 1", s.StatusText);

            var empty = Open();
            empty.Search();
            empty.FeedLrb(Lrb(1));
            empty.FeedLrf(Lrf(1, false, Token1, ""));
            Assert.Equal("No persisted logs matched.", empty.StatusText);

            var error = Open();
            error.Search();
            error.FeedLrb(Lrb(1));
            error.FeedLrx(Lrx(1, "Query timed out"));
            Assert.Equal("Query timed out", error.StatusText);
        }

        [Fact]
        public void DirtyDraftsShowAppliedSnapshotOnlyAndNeverChangeOnPaging()
        {
            var s = Open();
            Assert.False(s.IsDirty);
            s.Search();
            DeliverFreshPage(s, 1, MinimalRow(1), Token1, Token2, more: true);
            Assert.Equal("2026-09-25 12:00:00 → 2026-09-26 12:00:00", s.AppliedFilterDescription);
            Assert.False(s.IsDirty);

            s.Draft.Participant = "Edited";
            Assert.True(s.IsDirty);
            Assert.Equal("2026-09-25 12:00:00 → 2026-09-26 12:00:00", s.AppliedFilterDescription);

            s.Next();
            s.FeedLrb(Lrb(2));
            s.FeedLrd(Lrd(2, 0, 0, 1, B64(MinimalRow(2))));
            s.FeedLrf(Lrf(2, false, Token2, ""));
            Assert.Equal("2026-09-25 12:00:00 → 2026-09-26 12:00:00", s.AppliedFilterDescription);
            Assert.True(s.IsDirty);
            Assert.Equal("Edited", s.Draft.Participant);
        }
    }
}
