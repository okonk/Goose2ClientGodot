using System;
using System.Collections.Generic;
using System.Globalization;
using Goose2Client.Logs;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests
{
    public class LogQueryPacketTests
    {
        private const string Token = "AAECAwQFBgcICQoLDA0ODw";

        private static LogFreshFilterSnapshot Filter(long start, long end, string participant, int mapId, IReadOnlyList<int> typeIds, string text)
            => new LogFreshFilterSnapshot(start, end, participant, mapId, typeIds, text);

        [Fact]
        public void Fresh_FormatsExactPacket()
        {
            var sub = LogQuerySubmission.CreateFresh(10, 7, Filter(1000, 2000, " bob ", 3, new[] { 5, 2, 5 }, "hello"));
            var result = LogQueryPacket.Format(sub, 10);
            Assert.True(result.Success);
            Assert.Null(result.Error);
            Assert.Equal("LQS10,7,F,1000,2000,Ym9i,3,2|5,aGVsbG8=", result.Packet);
            Assert.Equal(9, result.Packet.Split(',').Length);
        }

        [Fact]
        public void Page_FormatsExactPacket()
        {
            var sub = LogQuerySubmission.CreatePage(10, 7, Token, LogNavigationIntent.Next);
            var result = LogQueryPacket.Format(sub);
            Assert.True(result.Success);
            Assert.Null(result.Error);
            Assert.Equal("LQS10,7,P," + Token, result.Packet);
            Assert.Equal(4, result.Packet.Split(',').Length);
        }

        [Fact]
        public void Page_SendsNoFilterMaterial()
        {
            var sub = LogQuerySubmission.CreatePage(1, 1, Token, LogNavigationIntent.Previous);
            var result = LogQueryPacket.Format(sub);
            Assert.True(result.Success);
            string[] fields = result.Packet.Split(',');
            Assert.Equal(4, fields.Length);
            Assert.Equal("LQS1", fields[0]);
            Assert.Equal("1", fields[1]);
            Assert.Equal("P", fields[2]);
            Assert.Equal(Token, fields[3]);
        }

        [Fact]
        public void Fresh_EmptyTypes_MeanAll()
        {
            var sub = LogQuerySubmission.CreateFresh(1, 1, Filter(0, 1, "", 0, Array.Empty<int>(), ""));
            var result = LogQueryPacket.Format(sub, 0);
            Assert.True(result.Success);
            Assert.Equal("LQS1,1,F,0,1,,0,,", result.Packet);
        }

        [Fact]
        public void Fresh_ParticipantIsTrimmed_TextIsLiteral()
        {
            var sub = LogQuerySubmission.CreateFresh(1, 1, Filter(0, 1, "  x  ", 0, Array.Empty<int>(), " pad "));
            var result = LogQueryPacket.Format(sub, 0);
            Assert.True(result.Success);
            Assert.Equal("LQS1,1,F,0,1,eA==,0,,IHBhZCA=", result.Packet);
        }

        [Fact]
        public void Fresh_SignedInvariantMilliseconds_UnderNonEnglishCulture()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var sub = LogQuerySubmission.CreateFresh(1, 1, Filter(-5, -4, "", 0, Array.Empty<int>(), ""));
                var result = LogQueryPacket.Format(sub, 0);
                Assert.True(result.Success);
                Assert.Equal("LQS1,1,F,-5,-4,,0,,", result.Packet);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void Packet_ExcludesTransportDelimiter()
        {
            var sub = LogQuerySubmission.CreateFresh(1, 1, Filter(0, 1, "", 0, Array.Empty<int>(), "a"));
            var result = LogQueryPacket.Format(sub, 0);
            Assert.True(result.Success);
            Assert.Equal("LQS1,1,F,0,1,,0,,YQ==", result.Packet);
            Assert.DoesNotContain((char)0x01, result.Packet);
        }

        [Fact]
        public void Participant_LimitIs64Utf8Bytes()
        {
            var ok = LogQuerySubmission.CreateFresh(1, 1, Filter(0, 1, new string('a', 64), 0, Array.Empty<int>(), ""));
            Assert.True(LogQueryPacket.Format(ok, 0).Success);
            var over = LogQuerySubmission.CreateFresh(1, 1, Filter(0, 1, new string('a', 65), 0, Array.Empty<int>(), ""));
            var result = LogQueryPacket.Format(over, 0);
            Assert.False(result.Success);
            Assert.Null(result.Packet);
            Assert.False(string.IsNullOrEmpty(result.Error));
        }

        [Fact]
        public void Text_LimitIs4096Utf8Bytes()
        {
            var ok = LogQuerySubmission.CreateFresh(1, 1, Filter(0, 1, "", 0, Array.Empty<int>(), new string('a', 4096)));
            Assert.True(LogQueryPacket.Format(ok, 0).Success);
            var over = LogQuerySubmission.CreateFresh(1, 1, Filter(0, 1, "", 0, Array.Empty<int>(), new string('a', 4097)));
            var result = LogQueryPacket.Format(over, 0);
            Assert.False(result.Success);
            Assert.Null(result.Packet);
            Assert.False(string.IsNullOrEmpty(result.Error));
        }

        [Fact]
        public void TypeCount_LimitedByCurrentLmtCount()
        {
            var types = new List<int> { 0, 1, 2, 3, 4 };
            var sub = LogQuerySubmission.CreateFresh(1, 1, Filter(0, 1, "", 0, types, ""));
            Assert.True(LogQueryPacket.Format(sub, 5).Success);
            var result = LogQueryPacket.Format(sub, 4);
            Assert.False(result.Success);
            Assert.Null(result.Packet);
            Assert.False(string.IsNullOrEmpty(result.Error));
        }

        [Fact]
        public void Packet_LimitIs8192Characters()
        {
            var types = new List<int>();
            for (int i = 0; i < 743; i++)
                types.Add(1000000000 + i);
            var sub = LogQuerySubmission.CreateFresh(1000, 1, Filter(0, 1, "", 0, types, ""));
            var result = LogQueryPacket.Format(sub, 743);
            Assert.True(result.Success);
            Assert.Equal(8192, result.Packet.Length);
            Assert.StartsWith("LQS1000,1,F,0,1,,0,1000000000|", result.Packet);

            types.Add(1000000743);
            var over = LogQuerySubmission.CreateFresh(1000, 1, Filter(0, 1, "", 0, types, ""));
            var failed = LogQueryPacket.Format(over, 744);
            Assert.False(failed.Success);
            Assert.Null(failed.Packet);
            Assert.False(string.IsNullOrEmpty(failed.Error));
        }

        [Fact]
        public void InvalidSubmissions_CannotBeConstructed()
        {
            var filter = Filter(0, 1, "", 0, Array.Empty<int>(), "");
            Assert.Throws<ArgumentOutOfRangeException>(() => LogQuerySubmission.CreateFresh(0, 1, filter));
            Assert.Throws<ArgumentOutOfRangeException>(() => LogQuerySubmission.CreateFresh(-1, 1, filter));
            Assert.Throws<ArgumentOutOfRangeException>(() => LogQuerySubmission.CreateFresh(1, 0, filter));
            Assert.Throws<ArgumentOutOfRangeException>(() => LogQuerySubmission.CreateFresh(1, -1, filter));
            Assert.Throws<ArgumentOutOfRangeException>(() => LogQuerySubmission.CreatePage(0, 1, Token, LogNavigationIntent.Next));
            Assert.Throws<ArgumentOutOfRangeException>(() => LogQuerySubmission.CreatePage(1, 0, Token, LogNavigationIntent.Next));
        }

        [Fact]
        public void InvalidFresh_RangesIdsAndTypes_ReturnSafeErrorAndNoPacket()
        {
            var equal = LogQueryPacket.Format(LogQuerySubmission.CreateFresh(1, 1, Filter(5, 5, "", 0, Array.Empty<int>(), "")), 0);
            Assert.False(equal.Success);
            Assert.Null(equal.Packet);
            Assert.False(string.IsNullOrEmpty(equal.Error));

            var reversed = LogQueryPacket.Format(LogQuerySubmission.CreateFresh(1, 1, Filter(10, 5, "", 0, Array.Empty<int>(), "")), 0);
            Assert.False(reversed.Success);
            Assert.Null(reversed.Packet);

            var negativeMap = LogQueryPacket.Format(LogQuerySubmission.CreateFresh(1, 1, Filter(0, 1, "", -1, Array.Empty<int>(), "")), 0);
            Assert.False(negativeMap.Success);
            Assert.Null(negativeMap.Packet);

            var negativeType = LogQueryPacket.Format(LogQuerySubmission.CreateFresh(1, 1, Filter(0, 1, "", 0, new[] { -1 }, "")), 0);
            Assert.False(negativeType.Success);
            Assert.Null(negativeType.Packet);
        }

        [Fact]
        public void InvalidPageToken_ReturnsSafeErrorAndNoPacket()
        {
            var shortToken = LogQueryPacket.Format(LogQuerySubmission.CreatePage(1, 1, "short", LogNavigationIntent.Next));
            Assert.False(shortToken.Success);
            Assert.Null(shortToken.Packet);
            Assert.False(string.IsNullOrEmpty(shortToken.Error));

            var plusToken = LogQueryPacket.Format(LogQuerySubmission.CreatePage(1, 1, Token.Replace('w', '+'), LogNavigationIntent.Previous));
            Assert.False(plusToken.Success);
            Assert.Null(plusToken.Packet);

            var longToken = LogQueryPacket.Format(LogQuerySubmission.CreatePage(1, 1, Token + "x", LogNavigationIntent.Next));
            Assert.False(longToken.Success);
            Assert.Null(longToken.Packet);
        }
    }
}
