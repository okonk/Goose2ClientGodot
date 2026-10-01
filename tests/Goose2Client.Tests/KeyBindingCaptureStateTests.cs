using System;
using System.Collections.Generic;
using Godot;
using Goose2Client.InputBindings;
using Xunit;

namespace Goose2Client.Tests;

public class KeyBindingCaptureStateTests
{
    private static KeyBindingCaptureState Armed()
    {
        var state = new KeyBindingCaptureState();
        state.Begin();
        state.Tick();
        return state;
    }

    [Fact]
    public void InitiatingPointer_DoesNotBecomeBinding_BeforeArmed()
    {
        var state = new KeyBindingCaptureState();
        state.Begin();

        state.KeyEvent(Key.A, pressed: true, echo: false, false, false, false, false);
        Assert.Null(state.Candidate);

        state.MouseButtonEvent(MouseButton.Left, pressed: true, false, false, false, false);
        Assert.Null(state.Candidate);
        Assert.True(state.WaitingInitiating);
    }

    [Fact]
    public void Capture_ArmsOnlyAfterInitiatingRelease()
    {
        var state = new KeyBindingCaptureState();
        state.Begin();

        state.MouseButtonEvent(MouseButton.Left, pressed: true, false, false, false, false);
        Assert.Null(state.Candidate);

        state.MouseButtonEvent(MouseButton.Left, pressed: false, false, false, false, false);
        Assert.False(state.WaitingInitiating);

        state.KeyEvent(Key.A, pressed: true, echo: false, false, false, false, false);
        Assert.Equal(new InputBinding.Keyboard(Key.A, false, false, false, false), state.Candidate);
    }

    [Fact]
    public void Capture_ArmsOnNextFrameSignal()
    {
        var state = new KeyBindingCaptureState();
        state.Begin();
        state.Tick();

        state.KeyEvent(Key.A, pressed: true, echo: false, false, false, false, false);
        Assert.Equal(new InputBinding.Keyboard(Key.A, false, false, false, false), state.Candidate);
    }

    [Fact]
    public void PressedNonEchoKey_CapturesExactModifiers()
    {
        var state = Armed();

        state.KeyEvent(Key.A, pressed: true, echo: false, ctrl: true, shift: true, alt: false, meta: true);

        Assert.Equal(new InputBinding.Keyboard(Key.A, true, true, false, true), state.Candidate);
        Assert.Equal(InputReleaseGate.ForPhysicalKey(Key.A), state.CandidateGate);
    }

    [Fact]
    public void KeyEcho_IsIgnored()
    {
        var state = Armed();

        state.KeyEvent(Key.A, pressed: true, echo: true, false, false, false, false);
        Assert.Null(state.Candidate);

        state.KeyEvent(Key.A, pressed: true, echo: false, false, false, false, false);
        Assert.Equal(new InputBinding.Keyboard(Key.A, false, false, false, false), state.Candidate);
    }

    [Theory]
    [InlineData(Key.Ctrl)]
    [InlineData(Key.Shift)]
    [InlineData(Key.Alt)]
    [InlineData(Key.Meta)]
    public void BareModifier_UpdatesWaitingStatusAndRemainsArmed(Key modifier)
    {
        var state = Armed();

        state.KeyEvent(modifier, pressed: true, echo: false,
            ctrl: modifier == Key.Ctrl,
            shift: modifier == Key.Shift,
            alt: modifier == Key.Alt,
            meta: modifier == Key.Meta);

        Assert.Null(state.Candidate);
        Assert.Contains("held", state.Prompt, StringComparison.Ordinal);

        state.KeyEvent(Key.A, pressed: true, echo: false, false, false, false, false);
        Assert.Equal(new InputBinding.Keyboard(Key.A, false, false, false, false), state.Candidate);
    }

    [Fact]
    public void Escape_IsAccepted()
    {
        var state = Armed();

        state.KeyEvent(Key.Escape, pressed: true, echo: false, false, false, false, false);

        Assert.Equal(new InputBinding.Keyboard(Key.Escape, false, false, false, false), state.Candidate);
    }

    [Fact]
    public void LeftAndRightClick_ReportReservedAndRemainArmed()
    {
        var state = Armed();

        state.MouseButtonEvent(MouseButton.Left, pressed: true, false, false, false, false);
        Assert.Null(state.Candidate);
        Assert.Contains("reserved", state.Prompt, StringComparison.Ordinal);

        state.MouseButtonEvent(MouseButton.Right, pressed: true, false, false, false, false);
        Assert.Null(state.Candidate);
        Assert.Contains("reserved", state.Prompt, StringComparison.Ordinal);

        state.KeyEvent(Key.A, pressed: true, echo: false, false, false, false, false);
        Assert.Equal(new InputBinding.Keyboard(Key.A, false, false, false, false), state.Candidate);
    }

    [Theory]
    [InlineData(MouseButton.Middle)]
    [InlineData(MouseButton.Xbutton1)]
    [InlineData(MouseButton.Xbutton2)]
    public void MiddleAndExtraButtons_CaptureWithReleaseGate(MouseButton button)
    {
        var state = Armed();

        state.MouseButtonEvent(button, pressed: true, ctrl: true, shift: false, alt: false, meta: false);

        Assert.Equal(new InputBinding.Mouse(button, true, false, false, false), state.Candidate);
        Assert.Equal(InputReleaseGate.ForMouseButton(button), state.CandidateGate);
    }

    [Fact]
    public void Wheel_CapturesWithNextFrameGate()
    {
        var state = Armed();

        state.MouseButtonEvent(MouseButton.WheelUp, pressed: true, false, false, false, false);

        Assert.Equal(new InputBinding.Mouse(MouseButton.WheelUp, false, false, false, false), state.Candidate);
        Assert.Equal(InputReleaseGate.NextFrame, state.CandidateGate);
    }

    [Fact]
    public void JoypadButton_CapturesWithoutDeviceIdentityButGateKeepsSourceDevice()
    {
        var state = Armed();

        state.JoypadButtonEvent(2, JoyButton.A, pressed: true);

        Assert.Equal(new InputBinding.JoypadButton(JoyButton.A), state.Candidate);
        Assert.Equal(InputReleaseGate.ForJoypadButton(2, JoyButton.A), state.CandidateGate);
    }

    [Fact]
    public void AxisBelowCaptureThreshold_IsIgnored()
    {
        var state = Armed();

        state.JoypadAxisEvent(0, JoyAxis.LeftX, 0f);
        state.JoypadAxisEvent(0, JoyAxis.LeftX, 0.74f);

        Assert.Null(state.Candidate);
    }

    [Fact]
    public void AxisCrossing_CapturesSign()
    {
        var positive = Armed();
        positive.JoypadAxisEvent(0, JoyAxis.LeftX, 0f);
        positive.JoypadAxisEvent(0, JoyAxis.LeftX, 0.8f);
        Assert.Equal(new InputBinding.JoypadAxis(JoyAxis.LeftX, 1), positive.Candidate);

        var negative = Armed();
        negative.JoypadAxisEvent(1, JoyAxis.RightY, 0f);
        negative.JoypadAxisEvent(1, JoyAxis.RightY, -0.9f);
        Assert.Equal(new InputBinding.JoypadAxis(JoyAxis.RightY, -1), negative.Candidate);
    }

    [Fact]
    public void AxisCrossingAtExactThreshold_IsCaptured()
    {
        var state = Armed();

        state.JoypadAxisEvent(0, JoyAxis.LeftX, 0f);
        state.JoypadAxisEvent(0, JoyAxis.LeftX, 0.75f);

        Assert.Equal(new InputBinding.JoypadAxis(JoyAxis.LeftX, 1), state.Candidate);
    }

    [Fact]
    public void AxisAtExactNeutral_UnblocksStartHeldAndUnseenPairs()
    {
        var startHeld = new KeyBindingCaptureState();
        startHeld.Begin();
        startHeld.SampleAxis(0, JoyAxis.LeftX, 0.9f);
        startHeld.Tick();

        startHeld.JoypadAxisEvent(0, JoyAxis.LeftX, 0.2f);
        Assert.Null(startHeld.Candidate);
        startHeld.JoypadAxisEvent(0, JoyAxis.LeftX, 0.8f);
        Assert.Equal(new InputBinding.JoypadAxis(JoyAxis.LeftX, 1), startHeld.Candidate);

        var unseen = Armed();
        unseen.JoypadAxisEvent(1, JoyAxis.RightY, 0.9f);
        Assert.Null(unseen.Candidate);
        unseen.JoypadAxisEvent(1, JoyAxis.RightY, -0.2f);
        Assert.Null(unseen.Candidate);
        unseen.JoypadAxisEvent(1, JoyAxis.RightY, -0.8f);
        Assert.Equal(new InputBinding.JoypadAxis(JoyAxis.RightY, -1), unseen.Candidate);
    }

    [Fact]
    public void AxisHeldAtCaptureStart_MustReturnToNeutralBeforeCapture()
    {
        var state = new KeyBindingCaptureState();
        state.Begin();
        state.SampleAxis(0, JoyAxis.LeftX, 0.5f);
        state.Tick();

        state.JoypadAxisEvent(0, JoyAxis.LeftX, 0.9f);
        Assert.Null(state.Candidate);

        state.JoypadAxisEvent(0, JoyAxis.LeftX, 0.1f);
        Assert.Null(state.Candidate);

        state.JoypadAxisEvent(0, JoyAxis.LeftX, 0.8f);
        Assert.Equal(new InputBinding.JoypadAxis(JoyAxis.LeftX, 1), state.Candidate);
        Assert.Equal(InputReleaseGate.ForJoypadAxis(0, JoyAxis.LeftX), state.CandidateGate);
    }

    [Fact]
    public void UnseenAxisPair_StartsBlockedUntilObservedNeutral()
    {
        var state = Armed();

        state.JoypadAxisEvent(3, JoyAxis.TriggerRight, 0.95f);
        Assert.Null(state.Candidate);

        state.JoypadAxisEvent(3, JoyAxis.TriggerRight, 0.15f);
        Assert.Null(state.Candidate);

        state.JoypadAxisEvent(3, JoyAxis.TriggerRight, 0.95f);
        Assert.Equal(new InputBinding.JoypadAxis(JoyAxis.TriggerRight, 1), state.Candidate);
    }

    [Fact]
    public void SecondCandidate_IsIgnoredWhileWaitingForRelease()
    {
        var state = Armed();
        state.KeyEvent(Key.A, pressed: true, echo: false, false, false, false, false);

        state.KeyEvent(Key.B, pressed: true, echo: false, false, false, false, false);
        state.MouseButtonEvent(MouseButton.Middle, pressed: true, false, false, false, false);

        Assert.Equal(new InputBinding.Keyboard(Key.A, false, false, false, false), state.Candidate);
    }

    [Fact]
    public void Cancel_BeforeCandidate_IsImmediateOnceInitiatingReleased()
    {
        var state = new KeyBindingCaptureState();
        state.Begin();
        state.MouseButtonEvent(MouseButton.Left, pressed: false, false, false, false, false);

        Assert.Equal(InputReleaseGate.Immediate, state.Cancel());
        Assert.False(state.IsActive);
    }

    [Fact]
    public void Cancel_WhileInitiatingStillHeld_WaitsForButtonRelease()
    {
        var state = new KeyBindingCaptureState();
        state.Begin();

        Assert.Equal(InputReleaseGate.ForMouseButton(MouseButton.Left), state.Cancel());
    }

    [Fact]
    public void Cancel_AfterAcceptedCandidate_ReturnsCandidateGate()
    {
        var state = Armed();
        state.KeyEvent(Key.A, pressed: true, echo: false, false, false, false, false);

        Assert.Equal(InputReleaseGate.ForPhysicalKey(Key.A), state.Cancel());
    }

    [Fact]
    public void Begin_WhileActive_Throws()
    {
        var state = new KeyBindingCaptureState();
        state.Begin();

        Assert.Throws<InvalidOperationException>(() => state.Begin());
    }

    [Fact]
    public void ReleaseGates_AreSatisfiedByInputState()
    {
        var input = new FakeReleaseState();

        Assert.True(InputReleaseGate.Immediate.IsSatisfied(input));
        Assert.True(InputReleaseGate.NextFrame.IsSatisfied(input));

        var keyGate = InputReleaseGate.ForPhysicalKey(Key.A);
        input.Keys.Add(Key.A);
        Assert.False(keyGate.IsSatisfied(input));
        input.Keys.Remove(Key.A);
        Assert.True(keyGate.IsSatisfied(input));

        var mouseGate = InputReleaseGate.ForMouseButton(MouseButton.Middle);
        input.MouseButtons.Add(MouseButton.Middle);
        Assert.False(mouseGate.IsSatisfied(input));
        input.MouseButtons.Remove(MouseButton.Middle);
        Assert.True(mouseGate.IsSatisfied(input));

        var joyButtonGate = InputReleaseGate.ForJoypadButton(1, JoyButton.B);
        input.JoyButtons.Add((1, JoyButton.B));
        Assert.False(joyButtonGate.IsSatisfied(input));
        input.JoyButtons.Remove((1, JoyButton.B));
        Assert.True(joyButtonGate.IsSatisfied(input));

        var axisGate = InputReleaseGate.ForJoypadAxis(0, JoyAxis.LeftX);
        input.Axes[(0, JoyAxis.LeftX)] = -0.5f;
        Assert.False(axisGate.IsSatisfied(input));
        input.Axes[(0, JoyAxis.LeftX)] = -0.2f;
        Assert.True(axisGate.IsSatisfied(input));
    }

    private sealed class FakeReleaseState : IInputReleaseState
    {
        public readonly HashSet<Key> Keys = new();
        public readonly HashSet<MouseButton> MouseButtons = new();
        public readonly HashSet<(int Device, JoyButton Button)> JoyButtons = new();
        public readonly Dictionary<(int Device, JoyAxis Axis), float> Axes = new();

        public bool IsPhysicalKeyPressed(Key key) => Keys.Contains(key);

        public bool IsMouseButtonPressed(MouseButton button) => MouseButtons.Contains(button);

        public bool IsJoyButtonPressed(int device, JoyButton button) => JoyButtons.Contains((device, button));

        public float GetJoyAxis(int device, JoyAxis axis) =>
            Axes.TryGetValue((device, axis), out var value) ? value : 0f;
    }
}
