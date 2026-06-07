using Godot;
using Goose2Client;

namespace Goose2Client.UI;

/// <summary>
/// Vitals window's static layered portrait display.
///
/// Renders the LocalPlayer's appearance data as a set of layered TextureRect
/// nodes (Body, Eyes, Hair, Chest, Helmet) inside the vitals portrait frame.
/// Appearance-data rendering is implemented later (Step 8 A1).
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

        // TODO(step8-A1): render vitals portrait layers from LocalPlayer appearance data
    }
}
