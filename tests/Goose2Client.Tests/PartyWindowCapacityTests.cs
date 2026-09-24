using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class PartyWindowCapacityTests
{
    [Fact]
    public void MaxMembers_MatchesServerDefault()
        => Assert.Equal(10, PartyWindow.MaxMembers);
}
