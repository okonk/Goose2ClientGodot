using System;
using System.Collections.Generic;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

public enum EnchantSlotRole
{
    Item,
    Essence,
    Protection
}

public readonly record struct EnchantTriple(int Item, int Essence, int Protection);

public class EnchantWindowState
{
    public int WindowId { get; set; }
    public int ItemSlot { get; private set; }
    public int EssenceSlot { get; private set; }
    public int ProtectionSlot { get; private set; }
    public bool CanEnchant { get; private set; }
    public string[] Lines { get; private set; } = Array.Empty<string>();
    public bool Pending { get; private set; }

    public bool CanConfirm => CanEnchant && !Pending;

    public EnchantTriple Place(EnchantSlotRole role, int slotId)
    {
        if (slotId > 0)
        {
            if (role != EnchantSlotRole.Item && ItemSlot == slotId) ItemSlot = 0;
            if (role != EnchantSlotRole.Essence && EssenceSlot == slotId) EssenceSlot = 0;
            if (role != EnchantSlotRole.Protection && ProtectionSlot == slotId) ProtectionSlot = 0;
        }
        Set(role, slotId);
        Pending = true;
        return Triple();
    }

    public EnchantTriple Clear(EnchantSlotRole role)
    {
        Set(role, 0);
        Pending = true;
        return Triple();
    }

    public EnchantTriple ConfirmTriple()
    {
        Pending = true;
        return Triple();
    }

    public EnchantSlotRole[] Apply(EnchantPreviewPacket p)
    {
        if (p.WindowId != WindowId) return Array.Empty<EnchantSlotRole>();

        var cleared = new List<EnchantSlotRole>(3);
        if (ItemSlot != 0 && p.ItemSlot == 0) cleared.Add(EnchantSlotRole.Item);
        if (EssenceSlot != 0 && p.EssenceSlot == 0) cleared.Add(EnchantSlotRole.Essence);
        if (ProtectionSlot != 0 && p.ProtectionSlot == 0) cleared.Add(EnchantSlotRole.Protection);

        ItemSlot = p.ItemSlot;
        EssenceSlot = p.EssenceSlot;
        ProtectionSlot = p.ProtectionSlot;
        CanEnchant = p.CanEnchant;
        Lines = p.Lines;
        Pending = false;
        return cleared.ToArray();
    }

    public void Reset()
    {
        WindowId = 0;
        ItemSlot = 0;
        EssenceSlot = 0;
        ProtectionSlot = 0;
        CanEnchant = false;
        Lines = Array.Empty<string>();
        Pending = false;
    }

    private void Set(EnchantSlotRole role, int id)
    {
        switch (role)
        {
            case EnchantSlotRole.Item: ItemSlot = id; break;
            case EnchantSlotRole.Essence: EssenceSlot = id; break;
            case EnchantSlotRole.Protection: ProtectionSlot = id; break;
        }
    }

    private EnchantTriple Triple() => new(ItemSlot, EssenceSlot, ProtectionSlot);
}
