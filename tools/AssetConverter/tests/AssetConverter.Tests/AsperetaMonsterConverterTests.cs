using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaMonsterConverterTests
{
    [Fact]
    public void CompiledEnc_Has66MonsterEntries()
    {
        var entries = AsperetaCompiledEnc.Load(Paths.AsperetaCompiledEnc);
        Assert.Equal(243, entries.Count);
        Assert.Equal(66, entries.Count(e => e.Type == AnimationType.Body && e.Id > 100));
    }

    [Fact]
    public void MonsterResources_HaveWalkAndAttackOnly_UnderOffsetIds()
    {
        var sheets = AsperetaSheets.Load(Paths.AsperetaData);
        var monsters = AsperetaMonsterConverter.BuildResources(
            AsperetaCompiledEnc.Load(Paths.AsperetaCompiledEnc), sheets, out var errors);

        Assert.Empty(errors);
        Assert.Equal(66, monsters.Count);

        var m = monsters.First();
        Assert.InRange(m.Id, 10101, 10166);
        Assert.Equal(AnimationType.Body, m.Type);
        Assert.StartsWith("Assets/Sprites/Bodies/1", m.RelativeOutputPath);

        var names = m.Animations.Select(a => a.Name).ToHashSet();
        foreach (var dir in new[] { "left", "down", "right", "up" })
        {
            Assert.Contains($"walk-no-equip-{dir}", names);
            Assert.Contains($"attack-no-equip-{dir}", names);
            Assert.DoesNotContain($"cast-{dir}", names);
        }
    }
}
