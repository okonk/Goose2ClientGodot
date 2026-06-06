namespace Goose2Client.UI;

/// <summary>
/// Implemented by server-spawned NPC windows (e.g. VendorWindow) that carry an NpcId.
/// </summary>
public interface INpcWindow
{
    int NpcId { get; }
}
