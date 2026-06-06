using Godot;
using Goose2Client.UI;
using Xunit;

public class CharacterEquipmentLayoutTests
{
    [Fact]
    public void SlotOffset_Index0_Helmet()
    {
        Assert.Equal(new Vector2(84, 34), CharacterEquipmentLayout.SlotOffset(0));
    }

    [Fact]
    public void SlotOffset_Index4_Weapon()
    {
        Assert.Equal(new Vector2(16, 104), CharacterEquipmentLayout.SlotOffset(4));
    }

    [Fact]
    public void SlotOffset_Index8_Shield()
    {
        Assert.Equal(new Vector2(152, 104), CharacterEquipmentLayout.SlotOffset(8));
    }

    [Fact]
    public void SlotOffset_Index13_Mount()
    {
        Assert.Equal(new Vector2(152, 174), CharacterEquipmentLayout.SlotOffset(13));
    }

    [Fact]
    public void SlotOffset_All14Distinct()
    {
        var positions = new Vector2[14];
        for (int i = 0; i < 14; i++)
            positions[i] = CharacterEquipmentLayout.SlotOffset(i);

        for (int i = 0; i < 14; i++)
            for (int j = i + 1; j < 14; j++)
                Assert.NotEqual(positions[i], positions[j]);
    }
}
