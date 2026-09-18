using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class CustomWindowMetricsTests
{
    [Fact]
    public void Defaults_AreWhiteWithDefaultAlpha()
    {
        Assert.Equal(255, CustomWindowMetrics.DefaultR);
        Assert.Equal(255, CustomWindowMetrics.DefaultG);
        Assert.Equal(255, CustomWindowMetrics.DefaultB);
        Assert.Equal(160, CustomWindowMetrics.DefaultA);
    }
}
