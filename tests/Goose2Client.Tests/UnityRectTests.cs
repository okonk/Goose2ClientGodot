using Goose2Client.UI;
using Xunit;

public class UnityRectTests
{
    [Fact]
    public void TopLeftAnchor_CenterPivot_Converts()
    {
        // CharacterCanvas NameText: parent 400x222, anchor (0,1), pivot (0.5,0.5),
        // anchoredPos (55.41,-15.59), size (100.82,11.18)  -> expect (5,10)
        var r = UnityRect.ToGodot(400, 222, 0f, 1f, 0.5f, 0.5f, 55.41f, -15.59f, 100.82f, 11.18f);
        Assert.Equal(5f, r.Left, 1);
        Assert.Equal(10f, r.Top, 1);
        Assert.Equal(100.82f, r.Width, 2);
        Assert.Equal(11.18f, r.Height, 2);
    }

    [Fact]
    public void CenterAnchor_CenterPivot_Converts()
    {
        // VitalsCanvas HP bar: parent 183x55, anchor (0.5,0.5), pivot (0.5,0.5),
        // anchoredPos (24,9), size (133,17) -> expect (49,10)
        var r = UnityRect.ToGodot(183, 55, 0.5f, 0.5f, 0.5f, 0.5f, 24f, 9f, 133f, 17f);
        Assert.Equal(49f, r.Left, 1);
        Assert.Equal(10f, r.Top, 1);
    }
}
