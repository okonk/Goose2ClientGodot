using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Goose2Client.Logs;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Tests
{
    public class LogResponseAssemblerTests
    {
        private const int Window = 5;
        private const int Request = 1;
        private const int MaxSegment = 12288;

        private static LogResultBegin Lrb(int request = Request) => LogPacketParsing.ParseLrb($"LRB{Window},{request}");
        private static LogResultData Lrd(int row, int index, int count, string segment) => LogPacketParsing.ParseLrd($"LRD{Window},{Request},{row},{index},{count},{segment}");
        private static LogResultFinish Lrf(bool more, string current, string next) => LogPacketParsing.ParseLrf($"LRF{Window},{Request},{(more ? "1" : "0")},{current},{next}");
        private static LogResultError Lrx(string message) => LogPacketParsing.ParseLrx($"LRX{Window},{Request},{Convert.ToBase64String(Encoding.UTF8.GetBytes(message))}");

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

        private static List<string> Split(string b64)
        {
            var list = new List<string>();
            for (int i = 0; i < b64.Length; i += MaxSegment)
                list.Add(b64.Substring(i, Math.Min(MaxSegment, b64.Length - i)));
            return list;
        }

        private static void FeedAll(LogResponseAssembler a, string json)
        {
            string b64 = B64(json);
            var chunks = Split(b64);
            Assert.True(a.FeedLrb(Lrb()));
            for (int i = 0; i < chunks.Count; i++)
                Assert.True(a.FeedLrd(Lrd(0, i, chunks.Count, chunks[i])));
        }

        private static LogRow SingleRow(long rowId = 1)
        {
            return new LogRow(
                rowId, 1700000000000L, 12L, true, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Player", LogEntityKind.Player, 1L, "A", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", "t");
        }

        private static string PaddedRow(long rowId)
        {
            return MinimalRow(rowId).Replace("\"originalText\":\"t\"", "\"originalText\":\"" + new string('p', 20000) + "\"");
        }

        private static LogRow PaddedSingleRow(long rowId)
        {
            return new LogRow(
                rowId, 1700000000000L, 12L, true, "Chat", "Communication", LogOtherIdKind.Player,
                new LogRowEntity("Player", LogEntityKind.Player, 1L, "A", true), null, null,
                new LogRowRaw(1, true, 2, true, 3, true, 4, true, 5, true), "s", new string('p', 20000));
        }

        [Fact]
        public void LrbStartsOneEmptyStageZeroRowsPlusLrfIsValid()
        {
            var a = new LogResponseAssembler();
            Assert.True(a.FeedLrb(Lrb()));
            Assert.True(a.HasStage);
            Assert.True(a.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
            Assert.NotNull(a.Result);
            var result = a.Result!;
            Assert.Empty(result.Rows);
            Assert.False(result.HasMore);
            Assert.Equal("AAECAwQFBgcICQoLDA0ODw", result.CurrentToken);
            Assert.Equal("", result.NextToken);
        }

        [Fact]
        public void TwoRowsMultipleChunksReconstructOnlyAfterContiguousIndexes()
        {
            var a = new LogResponseAssembler();
            string b641 = B64(PaddedRow(1));
            string b642 = B64(PaddedRow(2));
            var chunks1 = Split(b641);
            var chunks2 = Split(b642);
            Assert.True(chunks1.Count > 1);
            Assert.True(chunks2.Count > 1);
            Assert.True(a.FeedLrb(Lrb()));
            for (int i = 0; i < chunks1.Count; i++)
            {
                Assert.True(a.FeedLrd(Lrd(0, i, chunks1.Count, chunks1[i])));
                Assert.True(a.HasStage, "row must stay staged until finish");
            }
            for (int i = 0; i < chunks2.Count; i++)
                Assert.True(a.FeedLrd(Lrd(1, i, chunks2.Count, chunks2[i])));
            Assert.True(a.FeedLrf(Lrf(true, "AAECAwQFBgcICQoLDA0ODw", "BBECAwQFBgcICQoLDA0ODw")));
            var result = a.Result;
            Assert.Equal(2, result.Rows.Count);
            Assert.Equal(PaddedSingleRow(1), result.Rows[0]);
            Assert.Equal(PaddedSingleRow(2), result.Rows[1]);
            Assert.True(result.HasMore);
            Assert.Equal("BBECAwQFBgcICQoLDA0ODw", result.NextToken);
        }

        [Fact]
        public void Base64QuartetSplitsAcrossChunksPreserveExactBytes()
        {
            var a = new LogResponseAssembler();
            string json = MinimalRow(7).Replace("\"originalText\":\"t\"", "\"originalText\":\"" + new string('q', 10000) + "\"");
            string b64 = B64(json);
            var chunks = Split(b64);
            Assert.True(chunks.Count > 1);
            Assert.True(a.FeedLrb(Lrb()));
            for (int i = 0; i < chunks.Count; i++)
                Assert.True(a.FeedLrd(Lrd(0, i, chunks.Count, chunks[i])));
            Assert.True(a.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
            Assert.Equal(new string('q', 10000), a.Result.Rows[0].OriginalText);
        }

        [Fact]
        public void ChunksMayArriveAcrossCallsButRowOrderIsContiguous()
        {
            string b641 = B64(PaddedRow(1));
            string b642 = B64(PaddedRow(2));
            var c1 = Split(b641);
            var c2 = Split(b642);

            var ok = new LogResponseAssembler();
            Assert.True(ok.FeedLrb(Lrb()));
            for (int i = 0; i < c1.Count; i++)
                Assert.True(ok.FeedLrd(Lrd(0, i, c1.Count, c1[i])));
            for (int i = 0; i < c2.Count; i++)
                Assert.True(ok.FeedLrd(Lrd(1, i, c2.Count, c2[i])));
            Assert.True(ok.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
            Assert.Equal(2, ok.Result.Rows.Count);

            var interleaved = new LogResponseAssembler();
            Assert.True(interleaved.FeedLrb(Lrb()));
            Assert.True(interleaved.FeedLrd(Lrd(0, 0, c1.Count, c1[0])));
            Assert.False(interleaved.FeedLrd(Lrd(1, 0, c2.Count, c2[0])));
            Assert.False(interleaved.FeedLrd(Lrd(0, 1, c1.Count, c1[1])));
            Assert.False(interleaved.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
        }

        [Fact]
        public void OutOfOrderDuplicateSkippedReversedChunksAbortIrreversibly()
        {
            string b64 = B64(PaddedRow(1));
            var chunks = Split(b64);
            Assert.True(chunks.Count >= 2);

            var duplicate = new LogResponseAssembler();
            duplicate.FeedLrb(Lrb());
            Assert.True(duplicate.FeedLrd(Lrd(0, 0, chunks.Count, chunks[0])));
            Assert.False(duplicate.FeedLrd(Lrd(0, 0, chunks.Count, chunks[0])));

            var skipped = new LogResponseAssembler();
            skipped.FeedLrb(Lrb());
            Assert.False(skipped.FeedLrd(Lrd(0, 1, chunks.Count, chunks[1])));

            var reversed = new LogResponseAssembler();
            reversed.FeedLrb(Lrb());
            Assert.True(reversed.FeedLrd(Lrd(0, 0, chunks.Count, chunks[0])));
            Assert.True(reversed.FeedLrd(Lrd(0, 1, chunks.Count, chunks[1])));
            Assert.False(reversed.FeedLrd(Lrd(0, 0, chunks.Count, chunks[0])));

            var changedCount = new LogResponseAssembler();
            changedCount.FeedLrb(Lrb());
            Assert.True(changedCount.FeedLrd(Lrd(0, 0, chunks.Count, chunks[0])));
            Assert.False(changedCount.FeedLrd(Lrd(0, 1, chunks.Count + 1, chunks[1])));

            var skippedOrdinal = new LogResponseAssembler();
            skippedOrdinal.FeedLrb(Lrb());
            Assert.False(skippedOrdinal.FeedLrd(Lrd(1, 0, chunks.Count, chunks[0])));

            var repeatedOrdinal = new LogResponseAssembler();
            repeatedOrdinal.FeedLrb(Lrb());
            Assert.True(repeatedOrdinal.FeedLrd(Lrd(0, 0, chunks.Count, chunks[0])));
            Assert.False(repeatedOrdinal.FeedLrd(Lrd(0, 0, chunks.Count, chunks[0])));
        }

        [Fact]
        public void LrdBeforeOrAfterStageAndDuplicateLrbAbort()
        {
            string b64 = B64(PaddedRow(1));
            var chunks = Split(b64);

            var before = new LogResponseAssembler();
            Assert.False(before.FeedLrd(Lrd(0, 0, chunks.Count, chunks[0])));
            Assert.False(before.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));

            var after = new LogResponseAssembler();
            after.FeedLrb(Lrb());
            for (int i = 0; i < chunks.Count; i++)
                after.FeedLrd(Lrd(0, i, chunks.Count, chunks[i]));
            Assert.True(after.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
            Assert.False(after.FeedLrd(Lrd(0, 0, chunks.Count, chunks[0])));
            Assert.False(after.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));

            var duplicateBegin = new LogResponseAssembler();
            Assert.True(duplicateBegin.FeedLrb(Lrb()));
            Assert.False(duplicateBegin.FeedLrb(Lrb()));
            Assert.False(duplicateBegin.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
        }

        [Fact]
        public void LrfWithIncompleteRowAbortsIrreversibly()
        {
            string b64 = B64(PaddedRow(1));
            var chunks = Split(b64);
            var a = new LogResponseAssembler();
            a.FeedLrb(Lrb());
            for (int i = 0; i < chunks.Count - 1; i++)
                Assert.True(a.FeedLrd(Lrd(0, i, chunks.Count, chunks[i])));
            Assert.False(a.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
            Assert.Null(a.Result);
            Assert.False(a.FeedLrd(Lrd(0, chunks.Count - 1, chunks.Count, chunks[chunks.Count - 1])));
            Assert.False(a.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
        }

        [Fact]
        public void NoncanonicalBase64PaddingBitsAbortIrreversibly()
        {
            var a = new LogResponseAssembler();
            Assert.True(a.FeedLrb(Lrb()));
            Assert.True(a.FeedLrd(Lrd(0, 0, 2, "AA")));
            Assert.False(a.FeedLrd(Lrd(0, 1, 2, "==")));
            Assert.True(a.Aborted);
            Assert.False(a.HasStage);
            Assert.Null(a.Result);
            Assert.False(a.FeedLrd(Lrd(0, 0, 1, "AQ==")));
            Assert.False(a.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));

            var single = new LogResponseAssembler();
            Assert.True(single.FeedLrb(Lrb()));
            Assert.False(single.FeedLrd(Lrd(0, 0, 1, "AA==")));
            Assert.True(single.Aborted);
        }

        [Fact]
        public void MalformedSegmentBase64Utf8JsonShapeEnumOrNumberAborts()
        {
            var badSegment = new LogResponseAssembler();
            badSegment.FeedLrb(Lrb());
            Assert.False(badSegment.FeedLrd(Lrd(0, 0, 1, "QU!D")));
            Assert.False(badSegment.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));

            var badAggregate = new LogResponseAssembler();
            badAggregate.FeedLrb(Lrb());
            Assert.True(badAggregate.FeedLrd(Lrd(0, 0, 2, "QUJD")));
            Assert.False(badAggregate.FeedLrd(Lrd(0, 1, 2, "QUJ")));
            Assert.False(badAggregate.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));

            var badUtf8 = new LogResponseAssembler();
            string badUtf8Json = MinimalRow(1).Replace("\"originalText\":\"t\"", "\"originalText\":\"\\ud800\"");
            FeedAbortingRow(badUtf8, badUtf8Json);

            var badJson = new LogResponseAssembler();
            FeedAbortingRow(badJson, MinimalRow(1) + ",");

            var badShape = new LogResponseAssembler();
            FeedAbortingRow(badShape, MinimalRow(1).Replace("\"summary\":\"s\"", "\"summary\":12"));

            var badEnum = new LogResponseAssembler();
            FeedAbortingRow(badEnum, MinimalRow(1).Replace("\"otherIdKind\":\"Player\"", "\"otherIdKind\":\"Bogus\""));

            var badNumber = new LogResponseAssembler();
            FeedAbortingRow(badNumber, MinimalRow(1).Replace("\"rowId\":1", "\"rowId\":1.5"));
        }

        private static void FeedAbortingRow(LogResponseAssembler a, string json)
        {
            string b64 = B64(json);
            var chunks = Split(b64);
            Assert.True(a.FeedLrb(Lrb()));
            for (int i = 0; i < chunks.Count - 1; i++)
                Assert.True(a.FeedLrd(Lrd(0, i, chunks.Count, chunks[i])));
            Assert.False(a.FeedLrd(Lrd(0, chunks.Count - 1, chunks.Count, chunks[chunks.Count - 1])));
            Assert.False(a.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
        }

        [Fact]
        public void FiftyRowsPassRowFiftyOneAborts()
        {
            var a = new LogResponseAssembler();
            Assert.True(a.FeedLrb(Lrb()));
            for (int i = 0; i < 50; i++)
            {
                string b64 = B64(MinimalRow(i));
                Assert.True(a.FeedLrd(Lrd(i, 0, 1, b64)));
            }
            Assert.True(a.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
            Assert.Equal(50, a.Result.Rows.Count);

            var over = new LogResponseAssembler();
            over.FeedLrb(Lrb());
            for (int i = 0; i < 50; i++)
            {
                string b64 = B64(MinimalRow(i));
                Assert.True(over.FeedLrd(Lrd(i, 0, 1, b64)));
            }
            Assert.False(over.FeedLrd(Lrd(50, 0, 1, B64(MinimalRow(50)))));
            Assert.False(over.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
        }

        [Fact]
        public void DecodedRowByteBoundariesAreExact()
        {
            int jsonBase = MinimalRow(1).Length;
            int pad = 262144 - jsonBase + 1;
            string json = MinimalRow(1).Replace("\"originalText\":\"t\"", "\"originalText\":\"" + new string('a', pad) + "\"");
            Assert.Equal(262144, Encoding.UTF8.GetBytes(json).Length);

            var a = new LogResponseAssembler();
            FeedAll(a, json);
            Assert.True(a.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
            Assert.Equal(new string('a', pad), a.Result.Rows[0].OriginalText);

            var over = new LogResponseAssembler();
            string overJson = MinimalRow(1).Replace("\"originalText\":\"t\"", "\"originalText\":\"" + new string('a', pad + 1) + "\"");
            string overB64 = B64(overJson);
            var overChunks = Split(overB64);
            Assert.True(over.FeedLrb(Lrb()));
            for (int i = 0; i < overChunks.Count - 1; i++)
                Assert.True(over.FeedLrd(Lrd(0, i, overChunks.Count, overChunks[i])));
            Assert.False(over.FeedLrd(Lrd(0, overChunks.Count - 1, overChunks.Count, overChunks[overChunks.Count - 1])));
            Assert.False(over.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
        }

        [Fact]
        public void StagedAsciiByteBoundariesAreExact()
        {
            var a = new LogResponseAssembler();
            Assert.True(a.FeedLrb(Lrb()));
            for (int i = 0; i < 340; i++)
                Assert.True(a.FeedLrd(Lrd(0, i, 342, new string('A', 12288))));
            Assert.True(a.FeedLrd(Lrd(0, 340, 342, new string('A', 10349))));
            Assert.True(a.HasStage);
            Assert.False(a.FeedLrd(Lrd(0, 341, 342, "A")));
            Assert.False(a.HasStage);
            Assert.False(a.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
        }

        [Fact]
        public void LifecycleClearsEverythingAndAbortedStageCannotRecover()
        {
            string b64 = B64(PaddedRow(1));
            var chunks = Split(b64);

            var lrx = new LogResponseAssembler();
            lrx.FeedLrb(Lrb());
            lrx.FeedLrd(Lrd(0, 0, chunks.Count, chunks[0]));
            Assert.True(lrx.FeedLrx(Lrx("boom")));
            Assert.False(lrx.HasStage);
            Assert.Null(lrx.Result);
            Assert.False(lrx.FeedLrd(Lrd(0, 1, chunks.Count, chunks[1])));
            Assert.False(lrx.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));

            var aborted = new LogResponseAssembler();
            aborted.FeedLrb(Lrb());
            aborted.FeedLrd(Lrd(0, 1, chunks.Count, chunks[1]));
            Assert.False(aborted.HasStage);
            Assert.False(aborted.FeedLrd(Lrd(0, 0, chunks.Count, chunks[0])));
            Assert.False(aborted.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));

            var completed = new LogResponseAssembler();
            FeedAll(completed, MinimalRow(1));
            Assert.True(completed.FeedLrf(Lrf(false, "AAECAwQFBgcICQoLDA0ODw", "")));
            Assert.NotNull(completed.Result);
            Assert.True(completed.Abort());
            Assert.False(completed.HasStage);
            Assert.Null(completed.Result);

            var closed = new LogResponseAssembler();
            FeedAll(closed, MinimalRow(1));
            Assert.True(closed.OnClose());
            Assert.False(closed.HasStage);
            Assert.Null(closed.Result);

            var replaced = new LogResponseAssembler();
            FeedAll(replaced, MinimalRow(1));
            Assert.True(replaced.OnWindowReplacement());
            Assert.False(replaced.HasStage);
            Assert.Null(replaced.Result);

            var disposed = new LogResponseAssembler();
            FeedAll(disposed, MinimalRow(1));
            Assert.True(disposed.Dispose());
            Assert.False(disposed.HasStage);
            Assert.Null(disposed.Result);
        }
    }
}
