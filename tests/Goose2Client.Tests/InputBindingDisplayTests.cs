using System.Collections.Generic;
using System.Linq;
using Goose2Client.InputBindings;
using Godot;
using Xunit;

namespace Goose2Client.Tests;

public class InputBindingDisplayTests
{
    private sealed class FakeLabelProvider : IInputBindingLabelProvider
    {
        private readonly Dictionary<Key, string> _labels;

        public FakeLabelProvider(params (Key Key, string Label)[] labels) =>
            _labels = labels.ToDictionary(l => l.Key, l => l.Label);

        public string? GetKeyboardLabel(Key physicalKey) =>
            _labels.TryGetValue(physicalKey, out var label) ? label : null;
    }

    [Fact]
    public void Keyboard_UsesProviderLogicalLabel_WithoutRewritingPhysicalStorage()
    {
        var binding = new InputBinding.Keyboard(Key.A, true, false, false, false);

        var text = InputBindingDisplay.Format(binding, new FakeLabelProvider((Key.A, "a")));

        Assert.Equal("Ctrl+a", text);
        Assert.Equal(Key.A, binding.PhysicalKey);
    }

    [Fact]
    public void Keyboard_WithoutModifiers_ShowsBareLabel()
    {
        var binding = new InputBinding.Keyboard(Key.W, false, false, false, false);

        Assert.Equal("w", InputBindingDisplay.Format(binding, new FakeLabelProvider((Key.W, "w"))));
    }

    [Fact]
    public void Keyboard_WithAllModifiers_UsesFixedPlatformNeutralOrder()
    {
        var binding = new InputBinding.Keyboard(Key.X, true, true, true, true);

        Assert.Equal(
            "Ctrl+Shift+Alt+Meta+x",
            InputBindingDisplay.Format(binding, new FakeLabelProvider((Key.X, "x"))));
    }

    [Theory]
    [InlineData(Key.F5)]
    [InlineData(Key.F12)]
    public void Keyboard_UnknownLogicalLabel_FallsBackToPhysicalKeyName(Key key)
    {
        var binding = new InputBinding.Keyboard(key, false, false, false, false);

        Assert.Equal(key.ToString(), InputBindingDisplay.Format(binding, new FakeLabelProvider()));
    }

    [Fact]
    public void Keyboard_EmptyLogicalLabel_FallsBackToPhysicalKeyName()
    {
        var binding = new InputBinding.Keyboard(Key.F6, false, false, false, false);

        Assert.Equal("F6", InputBindingDisplay.Format(binding, new FakeLabelProvider((Key.F6, ""))));
    }

    [Theory]
    [InlineData(Key.Kp1, "1", "NP 1")]
    [InlineData(Key.Kp0, null, "NP 0")]
    [InlineData(Key.Kp9, "9", "NP 9")]
    [InlineData(Key.KpAdd, "Plus", "KpAdd")]
    [InlineData(Key.Comma, "Period", "Comma")]
    [InlineData(Key.Quoteleft, "Less", "Quoteleft")]
    [InlineData(Key.A, "A", "A")]
    [InlineData(Key.Key6, "6", "6")]
    [InlineData(Key.F5, "F5", "F5")]
    public void Keyboard_LayoutLabelTrustedOnlyForLettersAndDigits(Key key, string? layoutLabel, string expected)
    {
        var binding = new InputBinding.Keyboard(key, false, false, false, false);
        var provider = layoutLabel is null ? new FakeLabelProvider() : new FakeLabelProvider((key, layoutLabel));

        Assert.Equal(expected, InputBindingDisplay.Format(binding, provider));
    }

    [Fact]
    public void NumpadDigits_DistinctFromTopRowDigits()
    {
        var provider = new FakeLabelProvider((Key.Key1, "1"), (Key.Kp1, "1"));

        Assert.Equal("1", InputBindingDisplay.Format(new InputBinding.Keyboard(Key.Key1, false, false, false, false), provider));
        Assert.Equal("NP 1", InputBindingDisplay.Format(new InputBinding.Keyboard(Key.Kp1, false, false, false, false), provider));
    }

    [Theory]
    [InlineData(MouseButton.Middle, "Middle")]
    [InlineData(MouseButton.WheelUp, "WheelUp")]
    [InlineData(MouseButton.WheelDown, "WheelDown")]
    [InlineData(MouseButton.WheelLeft, "WheelLeft")]
    [InlineData(MouseButton.WheelRight, "WheelRight")]
    [InlineData(MouseButton.Xbutton1, "Xbutton1")]
    [InlineData(MouseButton.Xbutton2, "Xbutton2")]
    public void Mouse_StableNamesForMiddleExtraButtonsAndWheelDirections(MouseButton button, string expected)
    {
        var binding = new InputBinding.Mouse(button, false, false, false, false);

        Assert.Equal(expected, InputBindingDisplay.Format(binding, new FakeLabelProvider()));
    }

    [Fact]
    public void Mouse_WithModifiers_PrefixedInFixedOrder()
    {
        var binding = new InputBinding.Mouse(MouseButton.Middle, true, true, false, false);

        Assert.Equal("Ctrl+Shift+Middle", InputBindingDisplay.Format(binding, new FakeLabelProvider()));
    }

    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Right)]
    public void Mouse_LeftAndRight_CannotBeFormattedBecauseValidationRejectsThem(MouseButton button)
    {
        var result = InputBindingRules.NormalizeForEditor([new InputBinding.Mouse(button, false, false, false, false)]);

        Assert.False(result.Success);
    }

    [Fact]
    public void JoypadButtons_HaveDistinctStableText()
    {
        var provider = new FakeLabelProvider();
        var texts = new[] { JoyButton.A, JoyButton.B, JoyButton.Back, JoyButton.Guide }
            .Select(b => InputBindingDisplay.Format(new InputBinding.JoypadButton(b), provider))
            .ToArray();

        Assert.Equal(4, texts.Distinct().Count());
        Assert.Equal("Gamepad A", texts[0]);
        Assert.Equal("Gamepad B", texts[1]);
        Assert.Equal("Gamepad Back", texts[2]);
        Assert.Equal("Gamepad Guide", texts[3]);
    }

    [Fact]
    public void JoypadAxis_PositiveAndNegativeDirections_HaveDistinctStableText()
    {
        var provider = new FakeLabelProvider();
        var positive = InputBindingDisplay.Format(new InputBinding.JoypadAxis(JoyAxis.LeftX, 1), provider);
        var negative = InputBindingDisplay.Format(new InputBinding.JoypadAxis(JoyAxis.LeftX, -1), provider);
        var button = InputBindingDisplay.Format(new InputBinding.JoypadButton(JoyButton.A), provider);

        Assert.Equal("Gamepad LeftX +", positive);
        Assert.Equal("Gamepad LeftX -", negative);
        Assert.NotEqual(positive, negative);
        Assert.NotEqual(positive, button);
    }
}
