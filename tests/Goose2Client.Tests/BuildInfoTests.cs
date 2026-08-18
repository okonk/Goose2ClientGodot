using Goose2Client;
using Xunit;

namespace Goose2Client.Tests
{
    public class BuildInfoTests
    {
        [Fact]
        public void Normalize_NullInput_ReturnsDev()
        {
            Assert.Equal("dev", BuildInfo.Normalize(null));
        }

        [Fact]
        public void Normalize_EmptyOrWhitespace_ReturnsDev()
        {
            Assert.Equal("dev", BuildInfo.Normalize(""));
            Assert.Equal("dev", BuildInfo.Normalize("   \n"));
        }

        [Fact]
        public void Normalize_TrimsSurroundingWhitespace()
        {
            Assert.Equal("20260818T091305Z-40f2dbe", BuildInfo.Normalize("20260818T091305Z-40f2dbe\n"));
        }
    }
}
