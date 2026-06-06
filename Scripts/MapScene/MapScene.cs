using Godot;

namespace Goose2Client;

/// <summary>
/// World root for the active map.
/// Step 5 grows this into the full tilemap + entity container.
/// </summary>
public partial class MapScene : Node2D
{
    public override void _Ready()
    {
        GD.Print("Entered map");
    }
}
