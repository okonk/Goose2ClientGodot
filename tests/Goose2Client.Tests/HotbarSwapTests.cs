using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class HotbarSwapTests
{
    [Fact]
    public void Resolve_ItemToSpell_SwapsBoth()
    {
        var itemA = new ItemStats { SlotNumber = 1 };
        var spellB = new SpellInfo { SlotNumber = 2 };

        var target = HotbarContent.FromItem(itemA);
        var source = HotbarContent.FromSpell(spellB);

        var result = HotbarSwap.Resolve(target, source);

        Assert.Equal(HotbarContentKind.Spell, result.Target.Kind);
        Assert.Same(spellB, result.Target.Spell);
        Assert.Equal(HotbarContentKind.Item, result.Source.Kind);
        Assert.Same(itemA, result.Source.Item);
    }

    [Fact]
    public void Resolve_EmptyToItem_ItemMovesToTarget_SourceBecomesEmpty()
    {
        var itemA = new ItemStats { SlotNumber = 1 };

        var target = HotbarContent.Empty;
        var source = HotbarContent.FromItem(itemA);

        var result = HotbarSwap.Resolve(target, source);

        Assert.Equal(HotbarContentKind.Item, result.Target.Kind);
        Assert.Same(itemA, result.Target.Item);
        Assert.Equal(HotbarContentKind.Empty, result.Source.Kind);
    }

    [Fact]
    public void Resolve_SpellToSpell_SwapsSpells()
    {
        var spellA = new SpellInfo { SlotNumber = 1 };
        var spellB = new SpellInfo { SlotNumber = 2 };

        var target = HotbarContent.FromSpell(spellA);
        var source = HotbarContent.FromSpell(spellB);

        var result = HotbarSwap.Resolve(target, source);

        Assert.Equal(HotbarContentKind.Spell, result.Target.Kind);
        Assert.Same(spellB, result.Target.Spell);
        Assert.Equal(HotbarContentKind.Spell, result.Source.Kind);
        Assert.Same(spellA, result.Source.Spell);
    }
}
