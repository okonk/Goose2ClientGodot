using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Xunit;

namespace Goose2Client.Tests
{
    public class CharacterSettingsVisibilityTests
    {
        [Fact]
        public void WindowVisibility_RoundTrips()
        {
            var cs = new CharacterSettings
            {
                WindowSettings = new Dictionary<string, WindowSettings>
                {
                    { "Inventory", new WindowSettings { Position = new Vector2(10, 20), Visible = true } },
                    { "Character", new WindowSettings { Position = new Vector2(30, 40), Visible = false } },
                },
            };

            var json = JsonSerializer.Serialize(cs, CharacterSettings.JsonOptions);
            var back = CharacterSettings.FromJson(json);

            var inv = back.GetWindowSettings("Inventory");
            Assert.NotNull(inv);
            Assert.True(inv.Visible);
            Assert.Equal(new Vector2(10, 20), inv.Position);

            var chr = back.GetWindowSettings("Character");
            Assert.NotNull(chr);
            Assert.False(chr.Visible);   // default/false must also round-trip correctly
        }
    }
}
