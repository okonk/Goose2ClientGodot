using System;
using Goose2Client.Network;
using Xunit;

namespace Goose2Client.Network.Tests
{
    public class ProtocolTextCodecTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("hello")]
        [InlineData("a,b,c")]
        [InlineData("\u0001\u001f")]
        [InlineData("héllo €")]
        [InlineData("𝄞 music")]
        public void RoundTrips(string text)
        {
            Assert.True(ProtocolTextCodec.TryEncode(text, int.MaxValue, out string? b64));
            Assert.True(ProtocolTextCodec.TryDecode(b64, out string? decoded));
            Assert.Equal(text, decoded);
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("A", "QQ==")]
        [InlineData("AB", "QUI=")]
        [InlineData("ABC", "QUJD")]
        [InlineData("ABCD", "QUJDRA==")]
        [InlineData("hello", "aGVsbG8=")]
        public void Encode_ProducesCanonicalPaddedVectors(string text, string expected)
        {
            Assert.True(ProtocolTextCodec.TryEncode(text, int.MaxValue, out string? b64));
            Assert.Equal(expected, b64);
        }

        [Theory]
        [InlineData("QQ==", "A")]
        [InlineData("QUI=", "AB")]
        [InlineData("QUJD", "ABC")]
        [InlineData("QUJDRA==", "ABCD")]
        [InlineData("aGVsbG8=", "hello")]
        [InlineData("", "")]
        public void Decode_AcceptsCanonicalPaddedVectors(string b64, string expected)
        {
            Assert.True(ProtocolTextCodec.TryDecode(b64, out string? text));
            Assert.Equal(expected, text);
        }

        [Fact]
        public void Encode_EnforcesExactUtf8ByteLimit()
        {
            Assert.True(ProtocolTextCodec.TryEncode("a", 1, out string? one));
            Assert.Equal("YQ==", one);
            Assert.False(ProtocolTextCodec.TryEncode("a", 0, out _));
            Assert.False(ProtocolTextCodec.TryEncode("é", 1, out _));
            Assert.True(ProtocolTextCodec.TryEncode("é", 2, out string? two));
            Assert.True(ProtocolTextCodec.TryDecode(two, out string? back));
            Assert.Equal("é", back);
            Assert.False(ProtocolTextCodec.TryEncode("𝄞", 3, out _));
            Assert.True(ProtocolTextCodec.TryEncode("𝄞", 4, out _));
        }

        [Fact]
        public void Encode_RejectsLoneSurrogatesWithoutThrowing()
        {
            Assert.False(ProtocolTextCodec.TryEncode("\uD800", 8, out _));
            Assert.False(ProtocolTextCodec.TryEncode("\uD800ok", 8, out _));
        }

        [Theory]
        [InlineData("aGVsbG8")]
        [InlineData("aGVs bG8")]
        [InlineData(" aGVsbG8=")]
        [InlineData("aGVsbG8= ")]
        [InlineData("aGVs_bG8")]
        [InlineData("aGVs-bG8")]
        [InlineData("aGVs+bG8")]
        [InlineData("aGVs/bG8")]
        [InlineData("QQ=")]
        [InlineData("====")]
        [InlineData("QUJD=")]
        [InlineData("AB==")]
        [InlineData("A===")]
        [InlineData("QUJDQ3==")]
        [InlineData("QUJDé==")]
        public void Decode_RejectsMalformedBase64WithoutThrowing(string input)
        {
            Assert.False(ProtocolTextCodec.TryDecode(input, out _));
        }

        [Fact]
        public void Decode_RejectsInvalidUtf8()
        {
            string loneByte = Convert.ToBase64String(new byte[] { 0xFF, 0x21 });
            Assert.Equal("/yE=", loneByte);
            Assert.False(ProtocolTextCodec.TryDecode(loneByte, out _));

            string truncated = Convert.ToBase64String(new byte[] { 0x61, 0xC3 });
            Assert.False(ProtocolTextCodec.TryDecode(truncated, out _));
        }

        [Fact]
        public void Decode_RejectsNull()
        {
            Assert.False(ProtocolTextCodec.TryDecode(null, out _));
        }
    }
}
