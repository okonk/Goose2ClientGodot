using System.Linq;
using Goose2Client;
using Goose2Client.UI;
using Xunit;

public class ItemTooltipTextTests
{
    private static string ClassName(int id) => $"Class{id}";

    [Fact]
    public void Build_Weapon_damage_line()
    {
        var s = new ItemStats { MinDamage = 5, MaxDamage = 10, Delay = 15 };
        var lines = ItemTooltipText.Build(s, ClassName);

        var line = lines.FirstOrDefault(l => l.Color == ItemTooltipColor.WeaponDamage);
        Assert.Equal("5-10 Damage / 1.5s Delay", line.Text);
    }

    [Fact]
    public void Build_Armor_ac_line()
    {
        var s = new ItemStats { AC = 20 };
        var lines = ItemTooltipText.Build(s, ClassName);

        var line = lines.FirstOrDefault(l => l.Color == ItemTooltipColor.AC);
        Assert.Equal("20 Armor", line.Text);
    }

    [Fact]
    public void Build_Stacked_potion_value_gold()
    {
        var s = new ItemStats { Value = 1500 };
        var lines = ItemTooltipText.Build(s, ClassName);

        var line = lines.FirstOrDefault(l => l.Color == ItemTooltipColor.Value);
        Assert.Equal("Value: 1,500 gold", line.Text);
    }

    /// <summary>Fallback for a server too old to name the currency.</summary>
    [Fact]
    public void Build_Donation_item_value_credits()
    {
        var s = new ItemStats { Value = 200, Flags = ItemFlags.Donation };
        var lines = ItemTooltipText.Build(s, ClassName);

        var line = lines.FirstOrDefault(l => l.Color == ItemTooltipColor.Value);
        Assert.Equal("Value: 200 credits", line.Text);
    }

    [Fact]
    public void Build_Script_currency_named_by_the_server()
    {
        var s = new ItemStats { Value = 4500, CurrencyName = "spirit" };
        var lines = ItemTooltipText.Build(s, ClassName);

        var line = lines.FirstOrDefault(l => l.Color == ItemTooltipColor.Value);
        Assert.Equal("Value: 4,500 spirit", line.Text);
    }

    /// <summary>The server's name wins over the flag guess.</summary>
    [Fact]
    public void Build_Server_currency_beats_the_donation_flag()
    {
        var s = new ItemStats { Value = 200, Flags = ItemFlags.Donation, CurrencyName = "gold" };
        var lines = ItemTooltipText.Build(s, ClassName);

        var line = lines.FirstOrDefault(l => l.Color == ItemTooltipColor.Value);
        Assert.Equal("Value: 200 gold", line.Text);
    }

    [Fact]
    public void Build_Extra_stats_lines_in_wire_order()
    {
        var s = new ItemStats
        {
            ExtraStats = new[] { 400, 450, 1200, 800, 600, 350, 225, 2400, 150, 300, 90, 1500 }
        };
        var lines = ItemTooltipText.Build(s, ClassName);

        var statLines = lines.Where(l => l.Color == ItemTooltipColor.Stat).Select(l => l.Text).ToList();
        Assert.Equal(new[]
        {
            "+4% Melee Attack Speed",
            "+4.5% Spell Damage",
            "+12% Spell Critical Chance",
            "+8% Melee Damage",
            "+6% Melee Critical Chance",
            "+3.5% Damage Reduction",
            "+2.25% Health Regeneration",
            "+2,400 Health Regeneration",
            "+1.5% Mana Regeneration",
            "+300 Mana Regeneration",
            "+0.9% Spirit Regeneration",
            "+1,500 Spirit Regeneration",
        }, statLines);
    }

    [Fact]
    public void Build_Omits_zero_extra_stats()
    {
        var withZeros = new ItemStats
        {
            Description = "desc",
            AC = 20,
            Strength = 5,
            Value = 100,
            ExtraStats = new int[12],
        };
        var withDefault = new ItemStats
        {
            Description = "desc",
            AC = 20,
            Strength = 5,
            Value = 100,
        };

        var linesWithZeros = ItemTooltipText.Build(withZeros, ClassName);
        var linesWithDefault = ItemTooltipText.Build(withDefault, ClassName);

        Assert.Equal(linesWithDefault, linesWithZeros);
        Assert.Equal(1, linesWithZeros.Count(l => l.Color == ItemTooltipColor.Stat));
    }

    [Fact]
    public void Build_Formats_fractional_percent()
    {
        var s = new ItemStats { ExtraStats = new int[12] };
        s.ExtraStats[1] = 450;
        var lines = ItemTooltipText.Build(s, ClassName);

        var line = lines.FirstOrDefault(l => l.Color == ItemTooltipColor.Stat);
        Assert.Equal("+4.5% Spell Damage", line.Text);
    }

    [Fact]
    public void Build_Formats_negative_extra_stat()
    {
        var s = new ItemStats { ExtraStats = new int[12] };
        s.ExtraStats[0] = -400;
        var lines = ItemTooltipText.Build(s, ClassName);

        var line = lines.FirstOrDefault(l => l.Color == ItemTooltipColor.Stat);
        Assert.Equal("-4% Melee Attack Speed", line.Text);
    }

    [Fact]
    public void Build_Reports_both_regen_halves()
    {
        var s = new ItemStats { ExtraStats = new int[12] };
        s.ExtraStats[6] = 225;
        s.ExtraStats[7] = 2400;
        var lines = ItemTooltipText.Build(s, ClassName);

        var statLines = lines.Where(l => l.Color == ItemTooltipColor.Stat).Select(l => l.Text).ToList();
        Assert.Equal(new[] { "+2.25% Health Regeneration", "+2,400 Health Regeneration" }, statLines);
    }

    [Fact]
    public void Build_Places_extra_stats_before_the_requirements()
    {
        var s = new ItemStats { MinLevel = 10, MaxLevel = 20, ExtraStats = new int[12] };
        s.ExtraStats[3] = 800;
        var lines = ItemTooltipText.Build(s, ClassName);

        int statIndex = lines.FindIndex(l => l.Color == ItemTooltipColor.Stat);
        int reqIndex = lines.FindIndex(l => l.Color == ItemTooltipColor.Requirement);

        Assert.True(statIndex < reqIndex, $"Stat at {statIndex} should be before Requirement at {reqIndex}");
        Assert.Equal("+8% Melee Damage", lines[statIndex].Text);
    }

    [Fact]
    public void Build_Ignores_entries_beyond_the_label_table()
    {
        var s = new ItemStats { ExtraStats = Enumerable.Range(1, 14).ToArray() };
        var lines = ItemTooltipText.Build(s, ClassName);

        Assert.Equal(12, lines.Count(l => l.Color == ItemTooltipColor.Stat));
    }

    [Fact]
    public void Build_Class_restriction_positive_and_negative()
    {
        // Positive (offset 0): ClassRestrictions1=3
        var s1 = new ItemStats { ClassRestrictions1 = 3 };
        var lines1 = ItemTooltipText.Build(s1, ClassName);
        var reqLine1 = lines1.FirstOrDefault(l => l.Color == ItemTooltipColor.Requirement);
        Assert.StartsWith("You must be a Class3", reqLine1.Text);

        // Negative (offset -50): ClassRestrictions1=53 → className(53 + (-50)) = className(3) = "Class3"
        var s2 = new ItemStats { ClassRestrictions1 = 53 };
        var lines2 = ItemTooltipText.Build(s2, ClassName);
        var reqLine2 = lines2.FirstOrDefault(l => l.Color == ItemTooltipColor.Requirement);
        Assert.StartsWith("You must NOT be a Class3", reqLine2.Text);
    }

    [Fact]
    public void Build_Ordering_description_first_value_last()
    {
        var s = new ItemStats { Description = "desc", AC = 5, Value = 0 };
        var lines = ItemTooltipText.Build(s, ClassName);

        // First line is the description
        Assert.Equal("desc", lines[0].Text);
        Assert.Equal(ItemTooltipColor.Description, lines[0].Color);

        // Find indices of AC line and No Value line
        int acIndex = lines.FindIndex(l => l.Color == ItemTooltipColor.AC);
        int valueIndex = lines.FindIndex(l => l.Color == ItemTooltipColor.Value);

        // AC appears before the "No Value" line
        Assert.True(acIndex < valueIndex, $"AC at {acIndex} should be before Value at {valueIndex}");
        Assert.Equal("No Value", lines[valueIndex].Text);
    }
}
