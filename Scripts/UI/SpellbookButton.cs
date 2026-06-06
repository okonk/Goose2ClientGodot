using Godot;

namespace Goose2Client.UI;

/// <summary>
/// Button that serves as a drop target for spell reordering.
/// When a spell is dragged onto it, the spell is moved to the next/previous page.
/// </summary>
public partial class SpellbookButton : Button
{
    public bool IsNext { get; set; }
    public SpellbookWindow Window { get; set; } = null!;

    public override void _Ready()
    {
        base._Ready();
        Pressed += () => { if (IsNext) Window?.OnNextClicked(); else Window?.OnBackClicked(); };
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        return d.ContainsKey("kind") && d["kind"].AsString() == "spell";
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var d = data.AsGodotDictionary();
        var src = d["slot"].As<SpellSlot>();
        if (src != null && src.HasSpell)
            Window?.MoveSpell(src.SlotNumber, IsNext);
    }
}
