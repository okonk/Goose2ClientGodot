using System;
using System.Collections.Generic;
using Godot;

namespace Goose2Client.InputBindings;

public sealed class KeyBindingCaptureState
{
    public const float CaptureThreshold = 0.75f;
    public const float NeutralThreshold = 0.20f;

    private const string ArmedPrompt = "Press a key…";
    private const string ReservedPrompt = "Left and right mouse buttons are reserved.";

    private enum Phase
    {
        Inactive,
        WaitingInitiating,
        Armed,
        WaitingRelease
    }

    private readonly Dictionary<(int Device, JoyAxis Axis), bool> _axisEligible = new();
    private Phase _phase;
    private MouseButton _initiatingButton;
    private bool _initiatingHeld;
    private bool _ctrlHeld;
    private bool _shiftHeld;
    private bool _altHeld;
    private bool _metaHeld;
    private InputBinding? _candidate;
    private InputReleaseGate _candidateGate = InputReleaseGate.Immediate;
    private string _prompt = string.Empty;

    public bool IsActive => _phase != Phase.Inactive;

    public bool WaitingInitiating => _phase == Phase.WaitingInitiating;

    public InputBinding? Candidate => _candidate;

    public InputReleaseGate CandidateGate => _candidateGate;

    public string Prompt => _prompt;

    public void Begin(MouseButton initiatingButton = MouseButton.Left)
    {
        if (IsActive)
            throw new InvalidOperationException("Capture is already active.");

        _phase = Phase.WaitingInitiating;
        _initiatingButton = initiatingButton;
        _initiatingHeld = true;
        _ctrlHeld = _shiftHeld = _altHeld = _metaHeld = false;
        _candidate = null;
        _candidateGate = InputReleaseGate.Immediate;
        _axisEligible.Clear();
        _prompt = ArmedPrompt;
    }

    public void Tick()
    {
        if (_phase == Phase.WaitingInitiating)
            _phase = Phase.Armed;
    }

    public void SampleAxis(int device, JoyAxis axis, float value)
    {
        if (!IsActive)
            return;

        ObserveAxis(device, axis, value, canCapture: false);
    }

    public void KeyEvent(Key physicalKey, bool pressed, bool echo, bool ctrl, bool shift, bool alt, bool meta)
    {
        if (!IsActive || echo)
            return;

        if (IsModifierKey(physicalKey))
        {
            SetModifier(physicalKey, pressed);
            if (_phase == Phase.Armed)
                _prompt = BuildArmedPrompt();
            return;
        }

        if (!pressed || _phase != Phase.Armed)
            return;

        _candidate = new InputBinding.Keyboard(physicalKey, ctrl, shift, alt, meta);
        _candidateGate = InputReleaseGate.ForPhysicalKey(physicalKey);
        _phase = Phase.WaitingRelease;
        _prompt = $"Release {physicalKey} to confirm.";
    }

    public void MouseButtonEvent(MouseButton button, bool pressed, bool ctrl, bool shift, bool alt, bool meta)
    {
        if (!IsActive)
            return;

        if (button == _initiatingButton && _phase == Phase.WaitingInitiating)
        {
            _initiatingHeld = pressed;
            if (!pressed)
            {
                _phase = Phase.Armed;
                _prompt = BuildArmedPrompt();
            }
            return;
        }

        if (!pressed || _phase != Phase.Armed)
            return;

        if (button is MouseButton.Left or MouseButton.Right)
        {
            _prompt = ReservedPrompt;
            return;
        }

        _candidate = new InputBinding.Mouse(button, ctrl, shift, alt, meta);
        if (IsWheel(button))
        {
            _candidateGate = InputReleaseGate.NextFrame;
            _prompt = "Mouse wheel captured.";
        }
        else
        {
            _candidateGate = InputReleaseGate.ForMouseButton(button);
            _prompt = $"Release {button} to confirm.";
        }
        _phase = Phase.WaitingRelease;
    }

    public void JoypadButtonEvent(int device, JoyButton button, bool pressed)
    {
        if (!IsActive || !pressed || _phase != Phase.Armed)
            return;

        _candidate = new InputBinding.JoypadButton(button);
        _candidateGate = InputReleaseGate.ForJoypadButton(device, button);
        _phase = Phase.WaitingRelease;
        _prompt = $"Release {button} to confirm.";
    }

    public void JoypadAxisEvent(int device, JoyAxis axis, float value)
    {
        if (!IsActive || _phase != Phase.Armed)
            return;

        ObserveAxis(device, axis, value, canCapture: true);
    }

    public InputReleaseGate Cancel()
    {
        var gate = _phase == Phase.WaitingRelease
            ? _candidateGate
            : _initiatingHeld ? InputReleaseGate.ForMouseButton(_initiatingButton) : InputReleaseGate.Immediate;

        _phase = Phase.Inactive;
        _candidate = null;
        _candidateGate = InputReleaseGate.Immediate;
        _prompt = string.Empty;
        return gate;
    }

    private void ObserveAxis(int device, JoyAxis axis, float value, bool canCapture)
    {
        var magnitude = MathF.Abs(value);
        var eligible = _axisEligible.TryGetValue((device, axis), out var seen)
            ? seen || magnitude <= NeutralThreshold
            : magnitude <= NeutralThreshold;

        _axisEligible[(device, axis)] = eligible;
        if (!eligible || !canCapture || magnitude < CaptureThreshold)
            return;

        _candidate = new InputBinding.JoypadAxis(axis, value < 0 ? -1 : 1);
        _candidateGate = InputReleaseGate.ForJoypadAxis(device, axis);
        _phase = Phase.WaitingRelease;
        _prompt = $"Center {axis} to confirm.";
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta;

    private void SetModifier(Key key, bool held)
    {
        switch (key)
        {
            case Key.Ctrl:
                _ctrlHeld = held;
                break;
            case Key.Shift:
                _shiftHeld = held;
                break;
            case Key.Alt:
                _altHeld = held;
                break;
            case Key.Meta:
                _metaHeld = held;
                break;
        }
    }

    private string BuildArmedPrompt()
    {
        var modifiers = new List<string>();
        if (_ctrlHeld)
            modifiers.Add("Ctrl");
        if (_shiftHeld)
            modifiers.Add("Shift");
        if (_altHeld)
            modifiers.Add("Alt");
        if (_metaHeld)
            modifiers.Add("Meta");
        return modifiers.Count == 0
            ? ArmedPrompt
            : $"{ArmedPrompt} ({string.Join(", ", modifiers)} held)";
    }

    private static bool IsWheel(MouseButton button) =>
        button is MouseButton.WheelUp or MouseButton.WheelDown
            or MouseButton.WheelLeft or MouseButton.WheelRight;
}
