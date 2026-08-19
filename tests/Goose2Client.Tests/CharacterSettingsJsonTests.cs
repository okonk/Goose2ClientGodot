using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests
{
    public class CharacterSettingsJsonTests
    {
        [Fact]
        public void RoundTrip_SerializesAndDeserializesAllFields()
        {
            // Arrange — construct via parameterless ctor, assign fields directly
            var cs = new CharacterSettings
            {
                Hotkeys = new HotkeySetting[]
                {
                    new HotkeySetting(5, HotkeySetting.SlotType.Spell),
                    new HotkeySetting(12, HotkeySetting.SlotType.Item),
                    new HotkeySetting(-1, HotkeySetting.SlotType.Spell),
                },
                WindowSettings = new Dictionary<string, WindowSettings>
                {
                    { "Inventory", new WindowSettings { Position = new Vector2(12, 34) } },
                },
                Options = new Dictionary<string, object>
                {
                    { "showTooltips", true },
                    { "fontSize", 18 },
                    { "nickname", "TestPlayer" },
                },
                MountName = "SwiftHorse",
            };

            // Act — serialize then deserialize using the shared JsonOptions
            var json = JsonSerializer.Serialize(cs, CharacterSettings.JsonOptions);
            var back = JsonSerializer.Deserialize<CharacterSettings>(json, CharacterSettings.JsonOptions);

            // Assert — Hotkeys round-trip
            Assert.Equal(3, back.Hotkeys.Length);
            Assert.Equal(5, back.Hotkeys[0].SlotNumber);
            Assert.Equal(HotkeySetting.SlotType.Spell, back.Hotkeys[0].Type);
            Assert.Equal(12, back.Hotkeys[1].SlotNumber);
            Assert.Equal(HotkeySetting.SlotType.Item, back.Hotkeys[1].Type);
            Assert.Equal(-1, back.Hotkeys[2].SlotNumber);
            Assert.Equal(HotkeySetting.SlotType.Spell, back.Hotkeys[2].Type);

            // Assert — WindowSettings.Position round-trips (Godot.Vector2 via IncludeFields)
            Assert.True(back.WindowSettings.ContainsKey("Inventory"));
            Assert.Equal(12, back.WindowSettings["Inventory"].Position.X);
            Assert.Equal(34, back.WindowSettings["Inventory"].Position.Y);

            // Assert — MountName round-trips
            Assert.Equal("SwiftHorse", back.MountName);

            // Assert — GetOption handles JsonElement values (the critical trap)
            Assert.Equal(true, back.GetOption<bool>("showTooltips", false));
            Assert.Equal(18, back.GetOption<int>("fontSize", 0));
            Assert.Equal("TestPlayer", back.GetOption<string>("nickname", null));
        }

        [Fact]
        public void RoundTrip_PreservesWindowVisibility()
        {
            // Arrange — two windows with different Visible values
            var cs = new CharacterSettings
            {
                WindowSettings = new Dictionary<string, WindowSettings>
                {
                    { "Spellbook", new WindowSettings { Position = new Vector2(100, 200), Visible = true } },
                    { "Bank",      new WindowSettings { Position = new Vector2(300, 400), Visible = false } },
                },
            };

            // Act
            var json = JsonSerializer.Serialize(cs, CharacterSettings.JsonOptions);
            var back = JsonSerializer.Deserialize<CharacterSettings>(json, CharacterSettings.JsonOptions);

            // Assert — Visible survives round-trip for both entries
            Assert.True(back.WindowSettings.ContainsKey("Spellbook"));
            Assert.True(back.WindowSettings["Spellbook"].Visible);
            Assert.True(back.WindowSettings.ContainsKey("Bank"));
            Assert.False(back.WindowSettings["Bank"].Visible);
        }

        [Fact]
        public void RoundTrip_WindowCanvasSizeSurvivesWithPositionAndVisible()
        {
            // Arrange — a window saved on a 1080p canvas
            var cs = new CharacterSettings
            {
                WindowSettings = new Dictionary<string, WindowSettings>
                {
                    { "Hotbar", new WindowSettings { Position = new Vector2(520, 1039), Visible = true, CanvasSize = new Vector2I(1920, 1080) } },
                },
            };

            // Act — serialize, then deserialize through the FromJson entry point
            var json = JsonSerializer.Serialize(cs, CharacterSettings.JsonOptions);
            var back = CharacterSettings.FromJson(json);

            // Assert
            var ws = back.WindowSettings["Hotbar"];
            Assert.Equal(new Vector2I(1920, 1080), ws.CanvasSize);
            Assert.Equal(520, ws.Position.X);
            Assert.Equal(1039, ws.Position.Y);
            Assert.True(ws.Visible);
        }

        [Fact]
        public void LegacyJson_WithoutCanvasSizeField_DeserializesToZero()
        {
            // Hand-written JSON mirroring a pre-change user settings file (no CanvasSize key).
            const string legacyJson = """
                {
                    "Hotkeys": null,
                    "WindowSettings": {
                        "Hotbar": { "Position": { "X": 520.0, "Y": 679.0 }, "Visible": true }
                    },
                    "Options": null,
                    "MountName": null
                }
                """;

            var back = CharacterSettings.FromJson(legacyJson);
            var ws = back.WindowSettings["Hotbar"];

            // (0,0) is what BaseWindow maps to WindowPlacement.LegacyCanvas (1280x720);
            // combined with the Resolve identity test this proves old files place windows
            // exactly as before.
            Assert.Equal(default(Vector2I), ws.CanvasSize);
            Assert.Equal(520, ws.Position.X);
            Assert.Equal(679, ws.Position.Y);
            Assert.True(ws.Visible);
            Assert.Equal(new Vector2I(1280, 720), WindowPlacement.LegacyCanvas);
        }
    }
}
