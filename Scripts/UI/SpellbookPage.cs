using Godot;

namespace Goose2Client.UI;

/// <summary>
/// Plain C# container for a spellbook page — NOT a Node.
/// Holds the starting slot index, the slot array, and the UI container node.
/// </summary>
public sealed class SpellbookPage
{
    public int StartSlotIndex { get; init; }
    public SpellSlot[] Slots { get; init; } = null!;
    public Control Container { get; init; } = null!;
}
