using Goose2Client.Network.Packets;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.UI.Tests;

public class EnchantWindowStateTests
{
    private static EnchantPreviewPacket Ewp(int windowId, int item, int essence, int protection, bool canEnchant, params string[] lines)
        => new()
        {
            WindowId = windowId,
            ItemSlot = item,
            EssenceSlot = essence,
            ProtectionSlot = protection,
            CanEnchant = canEnchant,
            Lines = lines,
        };

    [Fact]
    public void Place_Item_ReturnsTriple()
    {
        var state = new EnchantWindowState();
        var triple = state.Place(EnchantSlotRole.Item, 5);
        Assert.Equal(new EnchantTriple(5, 0, 0), triple);
        Assert.Equal(5, state.ItemSlot);
        Assert.True(state.Pending);
    }

    [Fact]
    public void Place_SameIdIntoOtherRole_ClearsFirstRole()
    {
        var state = new EnchantWindowState();
        state.Place(EnchantSlotRole.Item, 5);
        var triple = state.Place(EnchantSlotRole.Essence, 5);
        Assert.Equal(new EnchantTriple(0, 5, 0), triple);
    }

    [Fact]
    public void Clear_ReturnsTripleWithZero()
    {
        var state = new EnchantWindowState();
        state.Place(EnchantSlotRole.Essence, 3);
        var triple = state.Clear(EnchantSlotRole.Essence);
        Assert.Equal(new EnchantTriple(0, 0, 0), triple);
        Assert.True(state.Pending);
    }

    [Fact]
    public void Apply_EchoedZeroReportsRoleCleared()
    {
        var state = new EnchantWindowState { WindowId = 1 };
        state.Place(EnchantSlotRole.Essence, 3);
        var cleared = state.Apply(Ewp(1, 0, 0, 0, true));
        Assert.Contains(EnchantSlotRole.Essence, cleared);
        Assert.DoesNotContain(EnchantSlotRole.Item, cleared);
        Assert.Equal(0, state.EssenceSlot);
        Assert.False(state.Pending);
    }

    [Fact]
    public void Apply_AdoptsSlotsLinesAndCanEnchant()
    {
        var state = new EnchantWindowState { WindowId = 1 };
        var cleared = state.Apply(Ewp(1, 5, 3, 7, true, "a", "b"));
        Assert.Empty(cleared);
        Assert.Equal(5, state.ItemSlot);
        Assert.Equal(3, state.EssenceSlot);
        Assert.Equal(7, state.ProtectionSlot);
        Assert.True(state.CanEnchant);
        Assert.Equal(new[] { "a", "b" }, state.Lines);
        Assert.False(state.Pending);
    }

    [Fact]
    public void Apply_AdoptsBreakPercentAndResetClearsIt()
    {
        var state = new EnchantWindowState { WindowId = 1 };
        var ewp = Ewp(1, 5, 3, 0, true, "a");
        ewp.BreakPercent = 40;
        state.Apply(ewp);
        Assert.Equal(40, state.BreakPercent);

        state.Reset();
        Assert.Equal(0, state.BreakPercent);
    }

    [Fact]
    public void Apply_StaleWindowId_Ignored()
    {
        var state = new EnchantWindowState { WindowId = 1 };
        state.Place(EnchantSlotRole.Item, 5);
        var cleared = state.Apply(Ewp(7, 9, 9, 9, false, "stale"));
        Assert.Empty(cleared);
        Assert.Equal(5, state.ItemSlot);
        Assert.False(state.CanEnchant);
        Assert.Empty(state.Lines);
        Assert.True(state.Pending);
    }

    [Fact]
    public void CanConfirm_FalseWhilePending_EvenIfLastEwpSaidYes()
    {
        var state = new EnchantWindowState { WindowId = 1 };
        state.Apply(Ewp(1, 5, 3, 7, true));
        Assert.True(state.CanConfirm);
        var triple = state.ConfirmTriple();
        Assert.Equal(new EnchantTriple(5, 3, 7), triple);
        Assert.False(state.CanConfirm);
    }

    [Fact]
    public void CanConfirm_FalseWhenServerSaidNo()
    {
        var state = new EnchantWindowState { WindowId = 1 };
        state.Apply(Ewp(1, 5, 3, 7, false));
        Assert.False(state.CanConfirm);
    }

    [Fact]
    public void Reset_ClearsAll()
    {
        var state = new EnchantWindowState { WindowId = 1 };
        state.Place(EnchantSlotRole.Item, 5);
        state.Place(EnchantSlotRole.Essence, 3);
        state.Place(EnchantSlotRole.Protection, 7);
        state.Apply(Ewp(1, 5, 3, 7, true, "a"));
        state.Reset();
        Assert.Equal(0, state.WindowId);
        Assert.False(state.CanEnchant);
        Assert.Empty(state.Lines);
        Assert.False(state.Pending);
        Assert.False(state.CanConfirm);
        Assert.Equal(new EnchantTriple(0, 0, 0), state.ConfirmTriple());
    }
}
