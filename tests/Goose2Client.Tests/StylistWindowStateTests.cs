using Goose2Client;
using Goose2Client.UI;
using Xunit;

public class StylistWindowStateTests
{
    [Fact]
    public void Open_selects_current_and_costs_nothing()
    {
        var s = new StylistWindowState();
        s.Open(33, 2, 1);
        Assert.Equal(33, s.CurrentHair);
        Assert.Equal(2, s.CurrentFace);
        Assert.Equal(33, s.SelectedHair);
        Assert.Equal(2, s.SelectedFace);
        Assert.Equal(0, s.Cost);
        Assert.False(s.CanChange);
    }

    [Fact]
    public void SetCurrent_keeps_browsed_selection()
    {
        var s = new StylistWindowState();
        s.Open(33, 2, 1);
        s.Select(40);
        s.SetCurrent(33, 2, 1);
        Assert.Equal(33, s.CurrentHair);
        Assert.Equal(2, s.CurrentFace);
        Assert.Equal(40, s.SelectedHair);
        Assert.Equal(2, s.SelectedFace);
        Assert.Equal(1000, s.Cost);
    }

    [Theory]
    [InlineData(33, 2, 33, 5, 1000)]
    [InlineData(33, 2, 40, 2, 1000)]
    [InlineData(33, 2, 40, 5, 2000)]
    public void Cost_counts_changed_slots(int hair, int face, int selHair, int selFace, int expected)
    {
        var s = new StylistWindowState();
        s.Open(hair, face, 1);
        s.Tab = StylistTab.Hair;
        s.Select(selHair);
        s.Tab = StylistTab.Face;
        s.Select(selFace);
        Assert.Equal(expected, s.Cost);
    }

    [Fact]
    public void Selecting_current_again_costs_nothing()
    {
        var s = new StylistWindowState();
        s.Open(33, 2, 1);
        s.Select(33);
        Assert.Equal(0, s.Cost);
        Assert.False(s.CanChange);
    }

    [Fact]
    public void Select_applies_to_active_tab()
    {
        var s = new StylistWindowState();
        s.Open(33, 2, 1);
        s.Tab = StylistTab.Hair;
        s.Select(40);
        Assert.Equal(40, s.SelectedHair);
        Assert.Equal(2, s.SelectedFace);
        s.Tab = StylistTab.Face;
        s.Select(7);
        Assert.Equal(40, s.SelectedHair);
        Assert.Equal(7, s.SelectedFace);
    }

    [Fact]
    public void Reset_restores_current()
    {
        var s = new StylistWindowState();
        s.Open(33, 2, 1);
        s.Tab = StylistTab.Hair;
        s.Select(40);
        s.Tab = StylistTab.Face;
        s.Select(7);
        s.Tab = StylistTab.Gender;
        s.Select(11);
        s.Reset();
        Assert.Equal(33, s.SelectedHair);
        Assert.Equal(2, s.SelectedFace);
        Assert.Equal(1, s.SelectedBody);
        Assert.Equal(0, s.Cost);
    }

    [Fact]
    public void Gold_one_short_cannot_change()
    {
        var s = new StylistWindowState();
        s.Open(33, 2, 1);
        s.Select(40);
        s.Gold = 999;
        Assert.Equal(1000, s.Cost);
        Assert.False(s.CanAfford);
        Assert.False(s.CanChange);
        s.Gold = 1000;
        Assert.True(s.CanAfford);
        Assert.True(s.CanChange);
    }

    [Fact]
    public void SetCurrent_to_selection_clears_cost()
    {
        var s = new StylistWindowState();
        s.Open(33, 2, 1);
        s.Select(40);
        Assert.Equal(1000, s.Cost);
        s.SetCurrent(40, 2, 1);
        Assert.Equal(0, s.Cost);
        Assert.False(s.CanChange);
    }

    [Fact]
    public void RotateRight_cycles_down_left_up_right()
    {
        var s = new StylistWindowState();
        Assert.Equal(Direction.Down, s.Facing);
        s.RotateRight();
        Assert.Equal(Direction.Left, s.Facing);
        s.RotateRight();
        Assert.Equal(Direction.Up, s.Facing);
        s.RotateRight();
        Assert.Equal(Direction.Right, s.Facing);
        s.RotateRight();
        Assert.Equal(Direction.Down, s.Facing);
    }

    [Fact]
    public void RotateLeft_cycles_down_right_up_left()
    {
        var s = new StylistWindowState();
        Assert.Equal(Direction.Down, s.Facing);
        s.RotateLeft();
        Assert.Equal(Direction.Right, s.Facing);
        s.RotateLeft();
        Assert.Equal(Direction.Up, s.Facing);
        s.RotateLeft();
        Assert.Equal(Direction.Left, s.Facing);
        s.RotateLeft();
        Assert.Equal(Direction.Down, s.Facing);
    }

    [Fact]
    public void Gender_tab_selects_body_and_costs_one_slot()
    {
        var s = new StylistWindowState();
        s.Open(33, 2, 1);
        s.Tab = StylistTab.Gender;
        s.Select(11);
        Assert.Equal(11, s.SelectedBody);
        Assert.Equal(33, s.SelectedHair);
        Assert.Equal(2, s.SelectedFace);
        Assert.Equal(11, s.Selected);
        Assert.Equal(1, s.Current);
        Assert.Equal(1000, s.Cost);
    }

    [Fact]
    public void Changing_all_three_costs_three_slots()
    {
        var s = new StylistWindowState();
        s.Open(33, 2, 1);
        s.Tab = StylistTab.Hair;
        s.Select(40);
        s.Tab = StylistTab.Face;
        s.Select(7);
        s.Tab = StylistTab.Gender;
        s.Select(11);
        Assert.Equal(3000, s.Cost);
    }
}
