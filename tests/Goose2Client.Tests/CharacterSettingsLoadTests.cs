using Xunit;

namespace Goose2Client.Tests
{
    public class CharacterSettingsLoadTests
    {
        [Fact]
        public void FromJson_NullInput_DoesNotThrowAndReturnsDefaults()
        {
            // Act
            var result = CharacterSettings.FromJson(null!);

            // Assert — no throw, all fields defaulted
            Assert.NotNull(result);
            Assert.NotNull(result.Hotkeys);
            Assert.InRange(result.Hotkeys.Length, 30, int.MaxValue);
            Assert.NotNull(result.WindowSettings);
            Assert.NotNull(result.Options);
        }

        [Fact]
        public void FromJson_CorruptJson_DoesNotThrowAndReturnsDefaults()
        {
            // Act
            var result = CharacterSettings.FromJson("this is not json");

            // Assert — no throw, all fields defaulted
            Assert.NotNull(result);
            Assert.NotNull(result.Hotkeys);
            Assert.InRange(result.Hotkeys.Length, 30, int.MaxValue);
            Assert.NotNull(result.WindowSettings);
            Assert.NotNull(result.Options);
        }

        [Fact]
        public void FromJson_EmptyObject_DoesNotThrowAndReturnsDefaults()
        {
            // Act
            var result = CharacterSettings.FromJson("{}");

            // Assert — all fields defaulted
            Assert.NotNull(result);
            Assert.NotNull(result.Hotkeys);
            Assert.InRange(result.Hotkeys.Length, 30, int.MaxValue);
            Assert.NotNull(result.WindowSettings);
            Assert.NotNull(result.Options);
        }

        [Fact]
        public void FromJson_ExplicitNulls_DefaultsAllFields()
        {
            // Act
            var result = CharacterSettings.FromJson("{\"Hotkeys\":null,\"WindowSettings\":null,\"Options\":null}");

            // Assert — all defaulted non-null
            Assert.NotNull(result);
            Assert.NotNull(result.Hotkeys);
            Assert.InRange(result.Hotkeys.Length, 30, int.MaxValue);
            Assert.NotNull(result.WindowSettings);
            Assert.NotNull(result.Options);
        }

        [Fact]
        public void FromJson_PartialHotkeys_PreservesEntryAndPadsTo30()
        {
            // Arrange — short array with one entry
            var json = "{\"Hotkeys\":[{\"SlotNumber\":5,\"Type\":0}]}";

            // Act
            var result = CharacterSettings.FromJson(json);

            // Assert — provided entry preserved at index 0, padded to >= 30
            Assert.NotNull(result);
            Assert.NotNull(result.Hotkeys);
            Assert.InRange(result.Hotkeys.Length, 30, int.MaxValue);
            Assert.Equal(5, result.Hotkeys[0].SlotNumber);
            Assert.Equal(HotkeySetting.SlotType.Spell, result.Hotkeys[0].Type);
            Assert.NotNull(result.WindowSettings);
            Assert.NotNull(result.Options);
        }
    }
}
