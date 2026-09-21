using Goose2Client.Character;
using Godot;
using Xunit;

public class CharacterAnchorTests
{
    [Theory]
    [InlineData(48, -24)]   // standard sprite: center 24px up so feet sit on the tile bottom
    [InlineData(64, -24)]   // h>=48 collapses to a constant -24 (taller sprites overhang downward)
    [InlineData(96, -24)]   // still -24
    [InlineData(32, -16)]   // short sprite: pure feet-align -h/2 = -16
    [InlineData(24, -12)]   // short sprite: -h/2 = -12
    public void OffsetY_aligns_feet_to_tile_bottom(int height, int expected)
        => Assert.Equal(expected, CharacterAnchor.OffsetY(height));

    [Theory]
    [InlineData(24, 48, 48)]
    [InlineData(49, 64, 64)]
    [InlineData(95, 95, 95)]
    [InlineData(48, 63, 63)]
    public void SpriteOffset_keeps_centered_frame_corners_on_whole_pixels(int width, int height, int anchorHeight)
    {
        var size = new Vector2(width, height);
        var topLeft = CharacterAnchor.SpriteOffset(anchorHeight, size) - size / 2f;

        Assert.Equal(topLeft.Round(), topLeft);
    }

    [Theory]
    [InlineData("mounted-walk-down", new int[] { 16, 17, 16, 17, 16 }, new int[] { 16, 17, 16, 17, 16 }, 0)]
    [InlineData("mounted-walk-left", new int[] { 8, 9, 10, 9, 10 }, new int[] { 9, 10, 9, 10, 9 }, 1)]
    [InlineData("mounted-walk-right", new int[] { 8, 9, 10, 9, 10 }, new int[] { 9, 10, 9, 10, 9 }, 1)]
    [InlineData("mounted-walk-up", new int[] { 10, 9, 11, 11, 11 }, new int[] { 9, 11, 11, 11, 11 }, -1)]
    public void Hair33_mounted_walk_stays_at_its_idle_offset_from_the_body(
        string animation, int[] bodyTop, int[] hairTop, int idleDifference)
    {
        for (int frame = 0; frame < bodyTop.Length; frame++)
            Assert.Equal(bodyTop[frame] + idleDifference,
                hairTop[frame] + CharacterAnchor.MountedHairYOffset(33, animation, frame));
    }

    [Theory]
    [InlineData(1, "mounted-walk-left", 2)]
    [InlineData(33, "walk-left", 2)]
    [InlineData(33, "mounted-idle-up", 0)]
    public void MountedHairYOffset_leaves_other_art_unchanged(int graphicId, string animation, int frame)
        => Assert.Equal(0, CharacterAnchor.MountedHairYOffset(graphicId, animation, frame));
}
