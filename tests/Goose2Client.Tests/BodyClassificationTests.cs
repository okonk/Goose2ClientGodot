using Goose2Client.Character;
using Xunit;

public class BodyClassificationTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(99, true)]
    [InlineData(100, false)]
    [InlineData(150, false)]
    [InlineData(9999, false)]
    [InlineData(10000, true)]
    [InlineData(10001, true)]
    [InlineData(10099, true)]
    [InlineData(10100, false)]
    [InlineData(10101, false)]
    public void IsLayered_boundary_matrix(int bodyId, bool expected)
        => Assert.Equal(expected, BodyClassification.IsLayered(bodyId));
}
