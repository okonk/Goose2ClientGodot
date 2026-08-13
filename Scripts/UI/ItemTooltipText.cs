using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Goose2Client.UI
{
    public enum ItemTooltipColor
    {
        Description,
        AC,
        WeaponDamage,
        HpMp,
        Stat,
        Resistance,
        Requirement,
        Effect,
        Value
    }

    /// <summary>Pure (Godot-free) helpers for building item-tooltip stat lines.</summary>
    public static class ItemTooltipText
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static string FormatNumber(int v)
        {
            if (v < 0)
                return v.ToString("N0", Inv);
            return "+" + v.ToString("N0", Inv);
        }

        /// <summary>Build ordered stat lines from item stats. No Godot types.</summary>
        public static List<(string Text, ItemTooltipColor Color)> Build(ItemStats s, Func<int, string> className)
        {
            var lines = new List<(string Text, ItemTooltipColor Color)>();

            if (s.Description != null)
                lines.Add((s.Description, ItemTooltipColor.Description));

            if (s.MaxDamage != 0)
                lines.Add((
                    $"{s.MinDamage.ToString("N0", Inv)}-{s.MaxDamage.ToString("N0", Inv)} Damage / {(s.Delay / 10.0f).ToString("F1", Inv)}s Delay",
                    ItemTooltipColor.WeaponDamage));

            if (s.AC != 0)
                lines.Add((
                    $"{s.AC.ToString("N0", Inv)} Armor",
                    ItemTooltipColor.AC));

            if (s.HP != 0)
                lines.Add((
                    $"{FormatNumber(s.HP)} Health",
                    ItemTooltipColor.HpMp));

            if (s.MP != 0)
                lines.Add((
                    $"{FormatNumber(s.MP)} Mana",
                    ItemTooltipColor.HpMp));

            if (s.SP != 0)
                lines.Add((
                    $"{FormatNumber(s.SP)} Spirit",
                    ItemTooltipColor.HpMp));

            if (s.Strength != 0)
                lines.Add((
                    $"{FormatNumber(s.Strength)} Strength",
                    ItemTooltipColor.Stat));

            if (s.Stamina != 0)
                lines.Add((
                    $"{FormatNumber(s.Stamina)} Stamina",
                    ItemTooltipColor.Stat));

            if (s.Intelligence != 0)
                lines.Add((
                    $"{FormatNumber(s.Intelligence)} Intelligence",
                    ItemTooltipColor.Stat));

            if (s.Dexterity != 0)
                lines.Add((
                    $"{FormatNumber(s.Dexterity)} Dexterity",
                    ItemTooltipColor.Stat));

            if (s.FireResist != 0)
                lines.Add((
                    $"{FormatNumber(s.FireResist)} Fire Resistance",
                    ItemTooltipColor.Resistance));

            if (s.WaterResist != 0)
                lines.Add((
                    $"{FormatNumber(s.WaterResist)} Water Resistance",
                    ItemTooltipColor.Resistance));

            if (s.EarthResist != 0)
                lines.Add((
                    $"{FormatNumber(s.EarthResist)} Earth Resistance",
                    ItemTooltipColor.Resistance));

            if (s.AirResist != 0)
                lines.Add((
                    $"{FormatNumber(s.AirResist)} Air Resistance",
                    ItemTooltipColor.Resistance));

            if (s.SpiritResist != 0)
                lines.Add((
                    $"{FormatNumber(s.SpiritResist)} Spirit Resistance",
                    ItemTooltipColor.Resistance));

            if (s.ClassRestrictions1 != 0)
            {
                int offset = s.ClassRestrictions1 < 50 ? 0 : -50;

                string classes = className(s.ClassRestrictions1 + offset);
                if (s.ClassRestrictions2 != 0)
                    classes += $"{(s.ClassRestrictions3 == 0 ? " or" : ",")} {className(s.ClassRestrictions2 + offset)}";
                if (s.ClassRestrictions3 != 0)
                    classes += $" or {className(s.ClassRestrictions3 + offset)}";

                lines.Add((
                    $"You must {(offset != 0 ? "NOT " : "")}be a {classes} to use this item",
                    ItemTooltipColor.Requirement));
            }

            if (s.MinLevel != 0 && s.MaxLevel != 0)
                lines.Add((
                    $"Requires level {s.MinLevel} to {s.MaxLevel}",
                    ItemTooltipColor.Requirement));
            else if (s.MinLevel == 0 && s.MaxLevel != 0)
                lines.Add((
                    $"Requires level 1 to {s.MaxLevel}",
                    ItemTooltipColor.Requirement));
            else if (s.MinLevel != 0 && s.MaxLevel == 0)
                lines.Add((
                    $"Requires level {s.MinLevel}",
                    ItemTooltipColor.Requirement));

            if (!string.IsNullOrEmpty(s.SpellEffect))
            {
                var effectLines = s.SpellEffect.Split(';');
                var effectChance = s.SpellEffectChance == 100 ? "" : $" ({s.SpellEffectChance}%)";

                lines.Add((
                    $"Effect: {effectLines[0]}{effectChance}",
                    ItemTooltipColor.Effect));

                for (int i = 1; i < effectLines.Length; i++)
                    lines.Add((
                        $"    {effectLines[i]}",
                        ItemTooltipColor.Effect));
            }

            // Blank separator
            lines.Add((" ", ItemTooltipColor.Description));

            if (s.Value == 0)
                lines.Add(("No Value", ItemTooltipColor.Value));
            else
            {
                // The server names the currency (gold, credits, or a script one like spirit).
                // Older servers do not send it, so the Donation flag stays as the fallback.
                var currency = !string.IsNullOrEmpty(s.CurrencyName)
                    ? s.CurrencyName
                    : s.Flags.HasFlag(ItemFlags.Donation) ? "credits" : "gold";
                lines.Add((
                    $"Value: {s.Value.ToString("N0", Inv)} {currency}",
                    ItemTooltipColor.Value));
            }

            return lines;
        }

        /// <summary>Human-readable item type string (e.g. "Cloth Chestpiece").</summary>
        public static string GetItemTypeText(ItemStats s)
        {
            return s.UseType switch
            {
                ItemUseType.Armor => $"{GetMaterialText(s.MaterialType)} {GetSlotText(s.SlotType)}".Trim(),
                ItemUseType.Weapon => GetMaterialText(s.MaterialType),
                ItemUseType.HairDye => "Hair Dye",
                ItemUseType.Letter => "Letter",
                ItemUseType.Money => "Money",
                ItemUseType.Recipe => "Recipe",
                ItemUseType.OneTime => " ",
                ItemUseType.Scroll => "Scroll",
                _ => "Miscellaneous",
            };
        }

        private static string GetMaterialText(ItemMaterial type)
        {
            return type switch
            {
                ItemMaterial.Cloth => "Cloth",
                ItemMaterial.Plate => "Plate",
                ItemMaterial.Leather => "Leather",
                ItemMaterial.Mail => "Mail",
                ItemMaterial.OneHandedSword => "One-Handed Sword",
                ItemMaterial.TwoHandedSword => "Two-Handed Sword",
                ItemMaterial.OneHandedBlunt => "One-Handed Blunt",
                ItemMaterial.TwoHandedBlunt => "Two-Handed Blunt",
                ItemMaterial.OneHandedPierce => "One-Handed Pierce",
                ItemMaterial.TwoHandedPierce => "Two-Handed Pierce",
                ItemMaterial.Fist => "Fist Weapon",
                _ => " "
            };
        }

        private static string GetSlotText(ItemSlotType slot)
        {
            return slot switch
            {
                ItemSlotType.None => " ",
                _ => slot.ToString()
            };
        }

        /// <summary>Returns comma-joined human-readable flag labels for the given flags.</summary>
        public static string GetFlagsText(ItemFlags flags)
        {
            var parts = new List<string>();
            foreach (ItemFlags value in Enum.GetValues(typeof(ItemFlags)))
            {
                if (flags.HasFlag(value))
                    parts.Add(MapFlagLabel(value));
            }
            return string.Join(", ", parts);
        }

        private static string MapFlagLabel(ItemFlags flag)
        {
            return flag switch
            {
                ItemFlags.BindOnEquip => "Bind on Equip",
                ItemFlags.BindOnPickup => "Bind on Pickup",
                ItemFlags.BreakOnDeath => "Break on Death",
                ItemFlags.ShortTerm => "Short Term",
                _ => flag.ToString()
            };
        }
    }
}
