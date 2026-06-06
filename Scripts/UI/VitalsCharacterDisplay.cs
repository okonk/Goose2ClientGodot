using Godot;
using Goose2Client;

namespace Goose2Client.UI;

/// <summary>
/// Step-7 stub for the character paper-doll display on the Character window.
/// 
/// Step-8 will render a static idle-down paper-doll from the LocalPlayer's
/// appearance data.  The live Character node does not re-expose its appearance
/// data in a way that is easy to duplicate into a SubViewport, so Step-7
/// resolves the data source here and leaves rendering as a TODO.
/// </summary>
public partial class VitalsCharacterDisplay : Control
{
    private Character.Character _localPlayer;

    /// <summary>
    /// Resolve the local player from GameManager and store the reference.
    /// Call this whenever appearance data might have changed.
    /// </summary>
    public void Refresh()
    {
        _localPlayer = GameManager.Instance.CurrentMapManager?.LocalPlayer;

        // TODO(step8): render static idle-down paper-doll from LocalPlayer appearance
    }
}
