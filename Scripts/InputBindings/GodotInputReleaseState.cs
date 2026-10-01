using Godot;

namespace Goose2Client.InputBindings;

public sealed class GodotInputReleaseState : IInputReleaseState
{
    public bool IsPhysicalKeyPressed(Key key) => Input.IsPhysicalKeyPressed(key);

    public bool IsMouseButtonPressed(MouseButton button) => Input.IsMouseButtonPressed(button);

    public bool IsJoyButtonPressed(int device, JoyButton button) => Input.IsJoyButtonPressed(device, button);

    public float GetJoyAxis(int device, JoyAxis axis) => Input.GetJoyAxis(device, axis);
}
