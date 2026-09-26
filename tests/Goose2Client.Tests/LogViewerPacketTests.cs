using System;
using System.Globalization;
using Goose2Client.Logs;
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests
{
    public class LogViewerPacketTests
    {
        private const string Token = "aaaaaaaaaaaaaaaaaaaaaa";

        private static T Parse<T>(PacketHandler handler, string packet)
            where T : class
        {
            return (T)handler.Parse(new PacketParser(packet, handler.Prefix));
        }

        [Fact]
        public void Lmt_ValidVector_ParsesAllFields()
        {
            string packet = "LMT5,12,QUJD,REVG";
            var p = Parse<LogTypeMetadata>(new LogTypeMetadataPacket(), packet);
            Assert.True(p.IsValid);
            Assert.Equal(5, p.WindowId);
            Assert.Equal(12, p.TypeId);
            Assert.Equal("ABC", p.Group);
            Assert.Equal("DEF", p.Label);
            Assert.Equal(0, p.RequestId);
            Assert.Equal(packet.Length, p.WireLength);
        }

        [Fact]
        public void Lmm_ValidVector_ParsesAllFields()
        {
            string packet = "LMM5,7,SG9tZQ==";
            var p = Parse<LogMapMetadata>(new LogMapMetadataPacket(), packet);
            Assert.True(p.IsValid);
            Assert.Equal(5, p.WindowId);
            Assert.Equal(7, p.MapId);
            Assert.Equal("Home", p.MapName);
            Assert.Equal(packet.Length, p.WireLength);
        }

        [Fact]
        public void Lmd_ValidVector_ParsesAllFields()
        {
            string packet = "LMD5,-1000,9999999999999";
            var p = Parse<LogDefaultsMetadata>(new LogDefaultsMetadataPacket(), packet);
            Assert.True(p.IsValid);
            Assert.Equal(5, p.WindowId);
            Assert.Equal(-1000L, p.StartUnixMs);
            Assert.Equal(9999999999999L, p.EndUnixMs);
            Assert.Equal(packet.Length, p.WireLength);
        }

        [Fact]
        public void Lrb_ValidVector_ParsesAllFields()
        {
            string packet = "LRB5,3";
            var p = Parse<LogResultBegin>(new LogResultBeginPacket(), packet);
            Assert.True(p.IsValid);
            Assert.Equal(5, p.WindowId);
            Assert.Equal(3, p.RequestId);
            Assert.Equal(packet.Length, p.WireLength);
        }

        [Fact]
        public void Lrd_ValidVector_CarriesOnlyIdentityOrdinalIndexCountAndRawSegment()
        {
            string packet = "LRD5,3,0,0,2,QUJD";
            var p = Parse<LogResultData>(new LogResultDataPacket(), packet);
            Assert.True(p.IsValid);
            Assert.Equal(5, p.WindowId);
            Assert.Equal(3, p.RequestId);
            Assert.Equal(0, p.RowOrdinal);
            Assert.Equal(0, p.ChunkIndex);
            Assert.Equal(2, p.ChunkCount);
            Assert.Equal("QUJD", p.Segment);
            Assert.Equal(packet.Length, p.WireLength);
        }

        [Fact]
        public void Lrf_ValidVector_HasMoreFalse_EmptyNextToken()
        {
            string packet = "LRF5,3,0," + Token + ",";
            var p = Parse<LogResultFinish>(new LogResultFinishPacket(), packet);
            Assert.True(p.IsValid);
            Assert.Equal(5, p.WindowId);
            Assert.Equal(3, p.RequestId);
            Assert.False(p.HasMore);
            Assert.Equal(Token, p.CurrentPageToken);
            Assert.Equal("", p.NextPageToken);
            Assert.Equal(packet.Length, p.WireLength);
        }

        [Fact]
        public void Lrf_ValidVector_HasMoreTrue_WithNextToken()
        {
            string packet = "LRF5,3,1," + Token + "," + Token;
            var p = Parse<LogResultFinish>(new LogResultFinishPacket(), packet);
            Assert.True(p.IsValid);
            Assert.True(p.HasMore);
            Assert.Equal(Token, p.CurrentPageToken);
            Assert.Equal(Token, p.NextPageToken);
        }

        [Fact]
        public void Lrx_ValidVector_ParsesAllFields()
        {
            string packet = "LRX5,3,TG9nIGVycm9y";
            var p = Parse<LogResultError>(new LogResultErrorPacket(), packet);
            Assert.True(p.IsValid);
            Assert.Equal(5, p.WindowId);
            Assert.Equal(3, p.RequestId);
            Assert.Equal("Log error", p.SafeMessage);
            Assert.Equal(packet.Length, p.WireLength);
        }

        [Fact]
        public void FieldCounts_AreExact()
        {
            Assert.False(Parse<LogTypeMetadata>(new LogTypeMetadataPacket(), "LMT5,12,QUJD").IsValid);
            Assert.False(Parse<LogTypeMetadata>(new LogTypeMetadataPacket(), "LMT5,12,QUJD,REVG,extra").IsValid);
            Assert.False(Parse<LogMapMetadata>(new LogMapMetadataPacket(), "LMM5,7").IsValid);
            Assert.False(Parse<LogMapMetadata>(new LogMapMetadataPacket(), "LMM5,7,SG9tZQ==,x").IsValid);
            Assert.False(Parse<LogDefaultsMetadata>(new LogDefaultsMetadataPacket(), "LMD5,1").IsValid);
            Assert.False(Parse<LogDefaultsMetadata>(new LogDefaultsMetadataPacket(), "LMD5,1,2,3").IsValid);
            Assert.False(Parse<LogResultBegin>(new LogResultBeginPacket(), "LRB5").IsValid);
            Assert.False(Parse<LogResultBegin>(new LogResultBeginPacket(), "LRB5,3,4").IsValid);
            Assert.False(Parse<LogResultData>(new LogResultDataPacket(), "LRD5,3,0,0,2").IsValid);
            Assert.False(Parse<LogResultData>(new LogResultDataPacket(), "LRD5,3,0,0,2,QUJD,x").IsValid);
            Assert.False(Parse<LogResultFinish>(new LogResultFinishPacket(), "LRF5,3,0," + Token).IsValid);
            Assert.False(Parse<LogResultFinish>(new LogResultFinishPacket(), "LRF5,3,0," + Token + "," + Token + ",x").IsValid);
            Assert.False(Parse<LogResultError>(new LogResultErrorPacket(), "LRX5,3").IsValid);
            Assert.False(Parse<LogResultError>(new LogResultErrorPacket(), "LRX5,3,QUJD,x").IsValid);
        }

        [Fact]
        public void Lrf_CurrentTokenIsRequired()
        {
            Assert.False(Parse<LogResultFinish>(new LogResultFinishPacket(), "LRF5,3,0,,").IsValid);
        }

        [Fact]
        public void Lrf_NextTokenIsCanonicalNonemptyOnlyWhenHasMore()
        {
            Assert.False(Parse<LogResultFinish>(new LogResultFinishPacket(), "LRF5,3,1," + Token + ",").IsValid);
            Assert.False(Parse<LogResultFinish>(new LogResultFinishPacket(), "LRF5,3,0," + Token + "," + Token).IsValid);
            Assert.False(Parse<LogResultFinish>(new LogResultFinishPacket(), "LRF5,3,1," + Token + ",short").IsValid);
            Assert.False(Parse<LogResultFinish>(new LogResultFinishPacket(), "LRF5,3,1," + Token + "," + Token + "x").IsValid);
            Assert.False(Parse<LogResultFinish>(new LogResultFinishPacket(), "LRF5,3,2," + Token + ",").IsValid);
            Assert.False(Parse<LogResultFinish>(new LogResultFinishPacket(), "LRF5,3,true," + Token + ",").IsValid);
        }

        [Fact]
        public void Lrf_RejectsMalformedTokenShape()
        {
            Assert.False(Parse<LogResultFinish>(new LogResultFinishPacket(), "LRF5,3,0,short,").IsValid);
            Assert.False(Parse<LogResultFinish>(new LogResultFinishPacket(), "LRF5,3,0," + Token.Replace('a', '+') + ",").IsValid);
            Assert.False(Parse<LogResultFinish>(new LogResultFinishPacket(), "LRF5,3,0," + Token.Replace('a', '=') + ",").IsValid);
        }

        [Fact]
        public void Parses_WithNonEnglishCurrentCulture()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var pmd = Parse<LogDefaultsMetadata>(new LogDefaultsMetadataPacket(), "LMD7,-100,9999999999999");
                Assert.True(pmd.IsValid);
                Assert.Equal(-100L, pmd.StartUnixMs);
                Assert.Equal(9999999999999L, pmd.EndUnixMs);
                var pld = Parse<LogResultData>(new LogResultDataPacket(), "LRD7,3,0,0,2,QUJD");
                Assert.True(pld.IsValid);
                Assert.Equal(2, pld.ChunkCount);
                var plb = Parse<LogResultBegin>(new LogResultBeginPacket(), "LRB7,2147483647");
                Assert.True(plb.IsValid);
                Assert.Equal(int.MaxValue, plb.RequestId);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void Identities_MustBePositiveAndInRange()
        {
            Assert.False(Parse<LogTypeMetadata>(new LogTypeMetadataPacket(), "LMT0,12,QUJD,REVG").IsValid);
            Assert.False(Parse<LogTypeMetadata>(new LogTypeMetadataPacket(), "LMT-5,12,QUJD,REVG").IsValid);
            Assert.False(Parse<LogTypeMetadata>(new LogTypeMetadataPacket(), "LMT2147483648,12,QUJD,REVG").IsValid);
            Assert.False(Parse<LogResultBegin>(new LogResultBeginPacket(), "LRB5,0").IsValid);
            Assert.False(Parse<LogResultBegin>(new LogResultBeginPacket(), "LRB5,-1").IsValid);
            Assert.False(Parse<LogResultBegin>(new LogResultBeginPacket(), "LRB5,2147483648").IsValid);

            var negativeTypeId = Parse<LogTypeMetadata>(new LogTypeMetadataPacket(), "LMT5,-12,QUJD,REVG");
            Assert.True(negativeTypeId.IsValid);
            Assert.Equal(-12, negativeTypeId.TypeId);
            var negativeMapId = Parse<LogMapMetadata>(new LogMapMetadataPacket(), "LMM5,-7,SG9tZQ==");
            Assert.True(negativeMapId.IsValid);
            Assert.Equal(-7, negativeMapId.MapId);
        }

        [Fact]
        public void Lrd_IndexesAreZeroBasedAndChunkCountIsPositive()
        {
            Assert.True(Parse<LogResultData>(new LogResultDataPacket(), "LRD5,3,0,0,1,QUJD").IsValid);
            Assert.False(Parse<LogResultData>(new LogResultDataPacket(), "LRD5,3,-1,0,1,QUJD").IsValid);
            Assert.False(Parse<LogResultData>(new LogResultDataPacket(), "LRD5,3,0,-1,1,QUJD").IsValid);
            Assert.False(Parse<LogResultData>(new LogResultDataPacket(), "LRD5,3,0,0,0,QUJD").IsValid);
            Assert.False(Parse<LogResultData>(new LogResultDataPacket(), "LRD5,3,0,0,-1,QUJD").IsValid);
            Assert.False(Parse<LogResultData>(new LogResultDataPacket(), "LRD5,3,0,0,1,").IsValid);
        }

        [Fact]
        public void Lrd_SegmentLimits()
        {
            string maxSegment = new string('A', 12288);
            Assert.True(Parse<LogResultData>(new LogResultDataPacket(), "LRD5,3,0,0,1," + maxSegment).IsValid);
            string overSegment = new string('A', 12289);
            Assert.False(Parse<LogResultData>(new LogResultDataPacket(), "LRD5,3,0,0,1," + overSegment).IsValid);
            Assert.False(Parse<LogResultData>(new LogResultDataPacket(), "LRD5,3,0,0,1,aé").IsValid);
            Assert.False(Parse<LogResultData>(new LogResultDataPacket(), "LRD5,3,0,0,1,A,B").IsValid);
        }

        [Fact]
        public void Lmd_Boundaries_MustFormIncreasingRange()
        {
            Assert.False(Parse<LogDefaultsMetadata>(new LogDefaultsMetadataPacket(), "LMD5,100,100").IsValid);
            Assert.False(Parse<LogDefaultsMetadata>(new LogDefaultsMetadataPacket(), "LMD5,200,100").IsValid);
            var extremes = Parse<LogDefaultsMetadata>(new LogDefaultsMetadataPacket(), "LMD5,-9223372036854775808,9223372036854775807");
            Assert.True(extremes.IsValid);
            Assert.Equal(long.MinValue, extremes.StartUnixMs);
            Assert.Equal(long.MaxValue, extremes.EndUnixMs);
            Assert.False(Parse<LogDefaultsMetadata>(new LogDefaultsMetadataPacket(), "LMD5,9223372036854775809,9223372036854775810").IsValid);
        }

        [Fact]
        public void MalformedTextBase64_IsInvalid()
        {
            Assert.False(Parse<LogTypeMetadata>(new LogTypeMetadataPacket(), "LMT5,12,!!!,REVG").IsValid);
            Assert.False(Parse<LogTypeMetadata>(new LogTypeMetadataPacket(), "LMT5,12,QUJD,REVG ").IsValid);
            Assert.False(Parse<LogMapMetadata>(new LogMapMetadataPacket(), "LMM5,7,SG9tZQ").IsValid);
            Assert.False(Parse<LogResultError>(new LogResultErrorPacket(), "LRX5,3,TG9").IsValid);
        }

        [Fact]
        public void InvalidPackets_PreserveIdentityAndWireLength()
        {
            string p1 = "LRD99,7,0,0,1,!!!";
            var d = Parse<LogResultData>(new LogResultDataPacket(), p1);
            Assert.False(d.IsValid);
            Assert.Equal(99, d.WindowId);
            Assert.Equal(7, d.RequestId);
            Assert.Equal(p1.Length, d.WireLength);

            string p2 = "LRD0,7,0,0,1,QUJD";
            var d2 = Parse<LogResultData>(new LogResultDataPacket(), p2);
            Assert.False(d2.IsValid);
            Assert.Equal(0, d2.WindowId);
            Assert.Equal(7, d2.RequestId);
            Assert.Equal(p2.Length, d2.WireLength);

            string p3 = "LRF12,4,0,short,";
            var f = Parse<LogResultFinish>(new LogResultFinishPacket(), p3);
            Assert.False(f.IsValid);
            Assert.Equal(12, f.WindowId);
            Assert.Equal(4, f.RequestId);
            Assert.Equal(p3.Length, f.WireLength);
            Assert.Equal("", f.CurrentPageToken);
            Assert.Equal("", f.NextPageToken);

            string p4 = "LMT3,12,QUJD,REVG";
            var t = Parse<LogTypeMetadata>(new LogTypeMetadataPacket(), p4);
            Assert.True(t.IsValid);
            Assert.Equal(p4.Length, t.WireLength);
            Assert.Equal(0, t.RequestId);
        }

        [Fact]
        public void PacketManager_ObserversReceiveInvalidPackets()
        {
            var manager = new PacketManager();
            object received = null;
            manager.Listen<LogResultErrorPacket>(o => received = o);
            manager.Handle("LRX0,1,QUJD");
            var p = Assert.IsType<LogResultError>(received);
            Assert.False(p.IsValid);
            Assert.Equal("LRX0,1,QUJD".Length, p.WireLength);
        }
    }
}
