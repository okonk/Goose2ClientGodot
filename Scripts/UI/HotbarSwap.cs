using Goose2Client;

namespace Goose2Client.UI;

public enum HotbarContentKind { Empty, Item, Spell }

public readonly struct HotbarContent
{
    public HotbarContentKind Kind { get; }
    public ItemStats Item { get; }
    public SpellInfo Spell { get; }

    private HotbarContent(HotbarContentKind kind, ItemStats item, SpellInfo spell)
    {
        Kind = kind;
        Item = item;
        Spell = spell;
    }

    public static HotbarContent Empty => new(HotbarContentKind.Empty, null, null);

    public static HotbarContent FromItem(ItemStats item)
        => new(HotbarContentKind.Item, item, null);

    public static HotbarContent FromSpell(SpellInfo spell)
        => new(HotbarContentKind.Spell, null, spell);
}

public static class HotbarSwap
{
    /// <summary>
    /// Drop a non-empty <paramref name="source"/> hotbar slot onto <paramref name="target"/>.
    /// Returns what each slot should hold afterward — faithful to Unity HotbarSlot.OnDrop
    /// (the <c>fromHotbar</c> branch).
    /// </summary>
    public static (HotbarContent Target, HotbarContent Source) Resolve(HotbarContent target, HotbarContent source)
    {
        var newTarget = source.Kind == HotbarContentKind.Item
            ? HotbarContent.FromItem(source.Item)
            : HotbarContent.FromSpell(source.Spell);

        var newSource = target.Kind switch
        {
            HotbarContentKind.Item  => HotbarContent.FromItem(target.Item),
            HotbarContentKind.Spell => HotbarContent.FromSpell(target.Spell),
            _                       => HotbarContent.Empty,
        };

        return (newTarget, newSource);
    }
}
