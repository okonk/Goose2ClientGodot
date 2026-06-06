using System;
using Xunit;

namespace Goose2Client.Network.Tests
{
    public class PacketParserTests
    {
        [Fact]
        public void GetInt32_ParsesIntegerToken()
        {
            // "PFX,42,rest" — prefix "PFX," so index starts at 4
            var parser = new PacketParser("PFX,42,rest", "PFX,");
            int value = parser.GetInt32();
            Assert.Equal(42, value);
        }

        [Fact]
        public void GetInt32_ParsesNegativeInteger()
        {
            var parser = new PacketParser("P,-17,end", "P,");
            Assert.Equal(-17, parser.GetInt32());
        }

        [Fact]
        public void GetBool_ReturnsFalseForZero()
        {
            var parser = new PacketParser("P,0,", "P,");
            Assert.False(parser.GetBool());
        }

        [Fact]
        public void GetBool_ReturnsTrueForNonZero()
        {
            var parser = new PacketParser("P,1,", "P,");
            Assert.True(parser.GetBool());

            var parser2 = new PacketParser("P,true,", "P,");
            Assert.True(parser2.GetBool());

            var parser3 = new PacketParser("P,anything,", "P,");
            Assert.True(parser3.GetBool());
        }

        [Fact]
        public void GetString_ReturnsNextToken()
        {
            var parser = new PacketParser("P,hello,world", "P,");
            Assert.Equal("hello", parser.GetString());
        }

        [Fact]
        public void GetString_LastTokenWithoutTrailingDelimiter()
        {
            var parser = new PacketParser("P,only", "P,");
            Assert.Equal("only", parser.GetString());
        }

        [Fact]
        public void GetString_SecondToken()
        {
            var parser = new PacketParser("P,first,second", "P,");
            parser.GetString(); // skip "first"
            Assert.Equal("second", parser.GetString());
        }

        [Fact]
        public void GetSubstring_ReturnsNCharsAndAdvances()
        {
            var parser = new PacketParser("PFX,abcde", "PFX,");
            string sub = parser.GetSubstring(3);
            Assert.Equal("abc", sub);
            // index should now be at 7 (4 + 3)
            Assert.Equal('d', parser.Peek());
        }

        [Fact]
        public void GetSubstring_OutOfBounds_Throws()
        {
            var parser = new PacketParser("PFX,abc", "PFX,");
            Assert.Throws<InvalidOperationException>(() => parser.GetSubstring(10));
        }

        [Fact]
        public void Peek_ReturnsCurrentCharWithoutAdvancing()
        {
            var parser = new PacketParser("PFX,abc", "PFX,");
            char c1 = parser.Peek();
            char c2 = parser.Peek();
            Assert.Equal('a', c1);
            Assert.Equal('a', c2); // same char, not advanced
        }

        [Fact]
        public void GetRemaining_ReturnsRestOfPacket()
        {
            var parser = new PacketParser("PFX,hello,world", "PFX,");
            Assert.Equal("hello,world", parser.GetRemaining());
        }

        [Fact]
        public void GetRemaining_AfterConsume()
        {
            var parser = new PacketParser("PFX,hello,world", "PFX,");
            parser.GetString(); // consumes "hello"
            Assert.Equal("world", parser.GetRemaining());
        }

        [Fact]
        public void GetRemaining_OutOfBounds_Throws()
        {
            var parser = new PacketParser("PFX,hi", "PFX,");
            parser.GetString(); // consumes "hi", index = packet.Length
            Assert.Throws<InvalidOperationException>(() => parser.GetRemaining());
        }

        [Fact]
        public void LengthRemaining_ReturnsCorrectCount()
        {
            var parser = new PacketParser("PFX,hello", "PFX,");
            Assert.Equal(5, parser.LengthRemaining()); // "hello"
        }

        [Fact]
        public void LengthRemaining_ZeroAfterExhaust()
        {
            var parser = new PacketParser("PFX,hi", "PFX,");
            parser.GetString(); // consumes "hi"
            Assert.Equal(0, parser.LengthRemaining());
        }

        [Fact]
        public void GetWholePacket_ReturnsOriginalPacket()
        {
            var parser = new PacketParser("PFX,42,true,hello", "PFX,");
            Assert.Equal("PFX,42,true,hello", parser.GetWholePacket());
        }

        [Fact]
        public void RoundTrip_MultiTypePacket()
        {
            // Simulate a real protocol packet: "MSG,1,true,hello,42"
            // Prefix is "MSG," (4 chars), so data starts at index 4
            string packet = "MSG,1,true,hello,42";
            var parser = new PacketParser(packet, "MSG,");

            int id = parser.GetInt32();
            bool active = parser.GetBool();
            string name = parser.GetString();
            int score = parser.GetInt32();

            Assert.Equal(1, id);
            Assert.True(active);
            Assert.Equal("hello", name);
            Assert.Equal(42, score);
            Assert.Equal(0, parser.LengthRemaining());
        }

        [Fact]
        public void RoundTrip_MixedSubstringAndTokens()
        {
            // "PFX,abc,123" — prefix "PFX,"
            var parser = new PacketParser("PFX,abc,123", "PFX,");

            string sub = parser.GetSubstring(2); // reads "ab"
            Assert.Equal("ab", sub);

            // Remaining from index 6: "c,123"
            // Peek should be 'c'
            Assert.Equal('c', parser.Peek());

            // GetNextToken reads until next ',' or end
            string token = parser.GetString(); // reads "c"
            Assert.Equal("c", token);

            int num = parser.GetInt32(); // reads "123"
            Assert.Equal(123, num);
        }

        [Fact]
        public void GetInt64_ParsesLargeInteger()
        {
            var parser = new PacketParser("P,9223372036854775807,end", "P,");
            Assert.Equal(long.MaxValue, parser.GetInt64());
        }

        [Fact]
        public void EmptyTokenBetweenDelimiters()
        {
            var parser = new PacketParser("P,,value", "P,");
            string empty = parser.GetString();
            Assert.Equal("", empty);
            Assert.Equal("value", parser.GetString());
        }

        [Fact]
        public void PrefixWithNoDataAfterThrowsOnGetRemaining()
        {
            var parser = new PacketParser("PFX", "PFX");
            Assert.Throws<InvalidOperationException>(() => parser.GetRemaining());
        }

        [Fact]
        public void CustomDelimiter()
        {
            var parser = new PacketParser("P|a|b|c", "P|");
            parser.Delimeter = '|';
            Assert.Equal("a", parser.GetString());
            Assert.Equal("b", parser.GetString());
            Assert.Equal("c", parser.GetString());
        }

        [Fact]
        public void GetNextToken_OutOfBounds_Throws()
        {
            // GetString calls GetNextToken internally
            var parser = new PacketParser("PFX,hi", "PFX,");
            parser.GetString(); // consumes "hi", index = packet.Length
            Assert.Throws<InvalidOperationException>(() => parser.GetString());
        }

        [Fact]
        public void Peek_OutOfBounds_Throws()
        {
            var parser = new PacketParser("PFX,hi", "PFX,");
            parser.GetString(); // consumes "hi", index = packet.Length
            Assert.Throws<IndexOutOfRangeException>(() => parser.Peek());
        }
    }
}
