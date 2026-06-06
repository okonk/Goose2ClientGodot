using Godot;

namespace Goose2Client.UI;

/// <summary>
/// Plain data class representing one hotbar page: 10 slots in a GridContainer.
/// </summary>
public sealed class HotbarPage
{
    public HotbarSlot[] Slots { get; init; }
    public Control Container { get; init; }
}
