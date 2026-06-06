using Goose2Client;

namespace Goose2Client.Character
{
    public static class AnimationNames
    {
        public static string DirectionString(Direction d) => d switch
        {
            Direction.Up => "up",
            Direction.Right => "right",
            Direction.Down => "down",
            Direction.Left => "left",
            _ => "down",
        };

        public static string Clip(string state, Direction d) => $"{state}-{DirectionString(d)}";
    }
}
