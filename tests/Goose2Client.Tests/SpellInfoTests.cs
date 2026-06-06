using System;
using Goose2Client;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Tests;

public class SpellInfoTests
{
    [Fact]
    public void FromPacket_MapsCooldownFromMilliseconds()
    {
        // Arrange
        var packet = new SpellbookSlotPacket
        {
            SlotNumber = 0,
            Name = "Fireball",
            TargetType = SpellTargetType.Player,
            GraphicId = 100,
            GraphicFile = 1,
            Cooldown = 2000,
        };

        // Act
        var spellInfo = SpellInfo.FromPacket(packet);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(2), spellInfo.Cooldown);
    }

    [Fact]
    public void FromPacket_CopiesNameAndTargetType()
    {
        // Arrange
        var packet = new SpellbookSlotPacket
        {
            SlotNumber = 5,
            Name = "Ice Storm",
            TargetType = SpellTargetType.NPC,
            GraphicId = 200,
            GraphicFile = 2,
            Cooldown = 5000,
        };

        // Act
        var spellInfo = SpellInfo.FromPacket(packet);

        // Assert
        Assert.Equal("Ice Storm", spellInfo.Name);
        Assert.Equal(SpellTargetType.NPC, spellInfo.TargetType);
    }

    [Fact]
    public void FromPacket_CopiesAllFields()
    {
        // Arrange
        var packet = new SpellbookSlotPacket
        {
            SlotNumber = 3,
            Name = "Heal",
            TargetType = SpellTargetType.Player,
            GraphicId = 50,
            GraphicFile = 3,
            Cooldown = 1500,
        };

        // Act
        var spellInfo = SpellInfo.FromPacket(packet);

        // Assert
        Assert.Equal(3, spellInfo.SlotNumber);
        Assert.Equal("Heal", spellInfo.Name);
        Assert.Equal(SpellTargetType.Player, spellInfo.TargetType);
        Assert.Equal(50, spellInfo.GraphicId);
        Assert.Equal(3, spellInfo.GraphicFile);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), spellInfo.Cooldown);
    }
}
