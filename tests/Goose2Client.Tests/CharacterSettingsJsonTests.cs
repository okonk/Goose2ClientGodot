using System.Collections.Generic;
using System.IO;
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

        [Fact]
        public void WindowSettings_SizeFactorPlaced_RoundTrip()
        {
            var cs = new CharacterSettings
            {
                WindowSettings = new Dictionary<string, WindowSettings>
                {
                    { "Hotbar", new WindowSettings
                        {
                            Position = new Vector2(520, 638),
                            Visible = true,
                            CanvasSize = new Vector2I(1280, 720),
                            Size = new Vector2(702, 72),
                            Factor = 2f,
                            Placed = true,
                        } },
                },
            };

            var json = JsonSerializer.Serialize(cs, CharacterSettings.JsonOptions);
            var back = CharacterSettings.FromJson(json);
            var ws = back.WindowSettings["Hotbar"];

            Assert.Equal(new Vector2(702, 72), ws.Size);
            Assert.Equal(2f, ws.Factor);
            Assert.True(ws.Placed);
            Assert.Equal(new Vector2(520, 638), ws.Position);
            Assert.True(ws.Visible);
            Assert.Equal(new Vector2I(1280, 720), ws.CanvasSize);
        }

        [Fact]
        public void WindowSettings_LegacyJsonWithoutSizeFactor()
        {
            // Pre-feature window section: no Size/Factor/Placed keys.
            const string legacyJson = """
                {
                    "Hotkeys": null,
                    "WindowSettings": {
                        "Hotbar": { "Position": { "X": 520.0, "Y": 679.0 }, "Visible": true, "CanvasSize": { "X": 1280, "Y": 720 } }
                    },
                    "Options": null,
                    "MountName": null
                }
                """;

            var back = CharacterSettings.FromJson(legacyJson);
            var ws = back.WindowSettings["Hotbar"];

            Assert.Equal(default(Vector2), ws.Size);
            Assert.Equal(0f, ws.Factor);
            Assert.False(ws.Placed);
            Assert.Equal(new Vector2(520, 679), ws.Position);
            Assert.True(ws.Visible);
            Assert.Equal(new Vector2I(1280, 720), ws.CanvasSize);
        }

        [Fact]
        public void WindowSettings_SavedOriginRoundTrips()
        {
            // (0, 0) with Placed = true is a VALID saved position, not an absence.
            var cs = new CharacterSettings
            {
                WindowSettings = new Dictionary<string, WindowSettings>
                {
                    { "Hotbar", new WindowSettings
                        {
                            Position = new Vector2(0, 0),
                            Visible = true,
                            CanvasSize = new Vector2I(1280, 720),
                            Size = new Vector2(351, 36),
                            Factor = 1f,
                            Placed = true,
                        } },
                },
            };

            var json = JsonSerializer.Serialize(cs, CharacterSettings.JsonOptions);
            var back = CharacterSettings.FromJson(json);
            var ws = back.WindowSettings["Hotbar"];

            Assert.True(ws.Placed);
            Assert.Equal(new Vector2(0, 0), ws.Position);
            Assert.Equal(new Vector2(351, 36), ws.Size);
            Assert.Equal(1f, ws.Factor);
        }

        [Fact]
        public void SetWindowVisible_PreservesFullQuad()
        {
            var dir = Path.Combine(Path.GetTempPath(), "gs2-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var cs = new TempCharacterSettings(dir);
                cs.SetWindowVisible("Hotbar", true);
                cs.SetWindowSetting("Hotbar", new Vector2(520, 638), new Vector2(702, 72), 2f, true, new Vector2I(1280, 720));

                var before = JsonSerializer.Serialize(cs, CharacterSettings.JsonOptions);
                cs.SetWindowVisible("Hotbar", false);
                var after = JsonSerializer.Serialize(cs, CharacterSettings.JsonOptions);

                // Byte-identical except the single Visible flip — the quad is untouched.
                Assert.Equal(before.Replace("\"Visible\":true", "\"Visible\":false"), after);

                var reloaded = new TempCharacterSettings(dir);
                Assert.True(reloaded.Load());
                var ws = reloaded.GetWindowSettings("Hotbar");
                Assert.NotNull(ws);
                Assert.False(ws.Visible);
                Assert.True(ws.Placed);
                Assert.Equal(new Vector2(520, 638), ws.Position);
                Assert.Equal(new Vector2(702, 72), ws.Size);
                Assert.Equal(2f, ws.Factor);
                Assert.Equal(new Vector2I(1280, 720), ws.CanvasSize);

                // First-time visibility write on an unplaced record leaves Placed == false.
                cs.SetWindowVisible("Bank", true);
                var again = new TempCharacterSettings(dir);
                Assert.True(again.Load());
                var bank = again.GetWindowSettings("Bank");
                Assert.NotNull(bank);
                Assert.True(bank.Visible);
                Assert.False(bank.Placed);
                Assert.Equal(default(Vector2), bank.Position);
                Assert.Equal(default(Vector2), bank.Size);
                Assert.Equal(0f, bank.Factor);
                Assert.Equal(default(Vector2I), bank.CanvasSize);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void SetWindowSetting_DragSave_UpdatesAllFiveAtomically()
        {
            var dir = Path.Combine(Path.GetTempPath(), "gs2-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var cs = new TempCharacterSettings(dir);
                cs.SetWindowVisible("Hotbar", false);

                // visible: null → the drag-end save must NOT touch the existing Visible.
                cs.SetWindowSetting("Hotbar", new Vector2(800, 600), new Vector2(400, 72), 2f, null, new Vector2I(1920, 1080));

                var reloaded = new TempCharacterSettings(dir);
                Assert.True(reloaded.Load());
                var ws = reloaded.GetWindowSettings("Hotbar");
                Assert.NotNull(ws);
                Assert.Equal(new Vector2(800, 600), ws.Position);
                Assert.Equal(new Vector2(400, 72), ws.Size);
                Assert.Equal(2f, ws.Factor);
                Assert.Equal(new Vector2I(1920, 1080), ws.CanvasSize);
                Assert.True(ws.Placed);
                Assert.False(ws.Visible);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private sealed class TempCharacterSettings : CharacterSettings
        {
            private readonly string _path;

            public TempCharacterSettings(string dir)
            {
                _path = Path.Combine(dir, "settings.json");
                ApplyDefaults();
            }

            protected override string GetFilePath() => _path;
        }
    }
}
