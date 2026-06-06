using Godot;

namespace Goose2Client
{
    public static partial class Helpers
    {
        public static int GetStackSplitAmount(int initialStack)
        {
            bool ctrl = Input.IsKeyPressed(Key.Ctrl);
            bool shift = Input.IsKeyPressed(Key.Shift);
            return StackSplit(initialStack, ctrl, shift);
        }
    }
}
