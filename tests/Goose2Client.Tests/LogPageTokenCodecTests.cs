using Goose2Client.Network;
using Xunit;

namespace Goose2Client.Network.Tests
{
    public class LogPageTokenCodecTests
    {
        [Theory]
        [InlineData("AAAAAAAAAAAAAAAAAAAAAA")]
        [InlineData("0123456789abcdefghijkl")]
        [InlineData("a-_B0123456789abcdefgh")]
        public void AcceptsCanonical22CharUnpaddedTokens(string token)
        {
            Assert.Equal(22, token.Length);
            Assert.True(LogPageTokenCodec.IsCanonical(token));
            Assert.True(LogPageTokenCodec.TryDecode(token, out byte[] bytes));
            Assert.Equal(16, bytes.Length);
        }

        [Fact]
        public void Decode_RecoversExactBytes()
        {
            Assert.True(LogPageTokenCodec.TryDecode("AAAAAAAAAAAAAAAAAAAAAA", out byte[] zeros));
            Assert.Equal(new byte[16], zeros);

            byte[] input = new byte[16];
            for (int i = 0; i < 16; i++)
                input[i] = (byte)i;
            Assert.True(LogPageTokenCodec.TryDecode("AAECAwQFBgcICQoLDA0ODw", out byte[] round));
            Assert.Equal(input, round);
        }

        [Theory]
        [InlineData("AAAAAAAAAAAAAAAAAAAAA")]
        [InlineData("AAAAAAAAAAAAAAAAAAAAAAA")]
        [InlineData("AAAAAAAAAAAAAAAAAAAA+A")]
        [InlineData("AAAAAAAAAAAAAAAAAAAA/A")]
        [InlineData("AAAAAAAAAAAAAAAAAAAA=A")]
        [InlineData("AAAAAAAAAAAAAAAAAAAA A")]
        [InlineData("AAAAAAAAAAAAAAAAAAAAA A")]
        [InlineData("AAAAAAAAAAAAAAAAAAAAé")]
        [InlineData("")]
        public void RejectsNoncanonicalSpellings(string token)
        {
            Assert.False(LogPageTokenCodec.IsCanonical(token));
            Assert.False(LogPageTokenCodec.TryDecode(token, out _));
        }

        [Fact]
        public void RejectsNull()
        {
            Assert.False(LogPageTokenCodec.IsCanonical(null));
            Assert.False(LogPageTokenCodec.TryDecode(null, out _));
        }
    }
}
