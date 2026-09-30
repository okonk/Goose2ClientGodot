using System;
using System.Collections.Generic;
using System.Linq;
using Goose2Client.InputBindings;
using Godot;
using Xunit;

namespace Goose2Client.Tests;

public class InputMapAdapterTests
{
    private const string NonCatalogAction = "ui_accept";

    [Fact]
    public void CaptureFactory_ReturnsCompletePhysicalSnapshot()
    {
        var set = BuildSet(
            ("Attack",
            [
                new InputBinding.Keyboard(Key.W, false, true, false, false),
                new InputBinding.Mouse(MouseButton.WheelUp, true, false, false, false),
                new InputBinding.JoypadButton(JoyButton.A),
                new InputBinding.JoypadAxis(JoyAxis.LeftX, -1)
            ]),
            ("MoveUp", [new InputBinding.Keyboard(Key.W, false, false, false, false)]));

        var surface = SeedSurface(set);
        var adapter = new GodotInputMapAdapter(surface);

        var captured = adapter.CaptureFactory(InputActionCatalog.Actions);

        foreach (var action in InputActionCatalog.Actions)
            Assert.Equal(set.GetBindings(action.Name), captured.GetBindings(action.Name));

        Assert.False(captured.TryGetBindings(NonCatalogAction, out _));
        Assert.Equal(0.5f, surface.GetActionDeadzone("Attack"));
    }

    [Fact]
    public void CaptureFactory_FailsWhenCatalogActionIsMissing()
    {
        var surface = SeedSurface(BuildSet());
        surface.Remove("Attack");

        var adapter = new GodotInputMapAdapter(surface);

        var ex = Assert.Throws<InputMapConfigurationException>(() => adapter.CaptureFactory(InputActionCatalog.Actions));
        Assert.Contains("Attack", ex.Message);
    }

    [Fact]
    public void CaptureFactory_FailsOnLogicalKeyboardEvent()
    {
        var surface = SeedSurface(BuildSet());
        surface.ReplaceEvents("Attack", KeyboardDescriptor(Key.None, Key.A, 0));

        var adapter = new GodotInputMapAdapter(surface);

        var ex = Assert.Throws<InputMapConfigurationException>(() => adapter.CaptureFactory(InputActionCatalog.Actions));
        Assert.Contains("Attack", ex.Message);
    }

    [Fact]
    public void CaptureFactory_FailsOnUnicodeKeyboardEvent()
    {
        var surface = SeedSurface(BuildSet());
        surface.ReplaceEvents("Attack", KeyboardDescriptor(Key.None, Key.None, 65));

        var adapter = new GodotInputMapAdapter(surface);

        var ex = Assert.Throws<InputMapConfigurationException>(() => adapter.CaptureFactory(InputActionCatalog.Actions));
        Assert.Contains("Attack", ex.Message);
    }

    [Fact]
    public void CaptureFactory_FailsOnReservedMouseButtons()
    {
        var surface = SeedSurface(BuildSet());
        surface.ReplaceEvents("Attack", InputMapEventDescriptor.Mouse(MouseButton.Left, false, false, false, false));

        var adapter = new GodotInputMapAdapter(surface);

        var ex = Assert.Throws<InputMapConfigurationException>(() => adapter.CaptureFactory(InputActionCatalog.Actions));
        Assert.Contains("Attack", ex.Message);
    }

    [Fact]
    public void CaptureFactory_FailsOnNonCanonicalAxisValue()
    {
        var surface = SeedSurface(BuildSet());
        surface.ReplaceEvents("Attack", new InputMapEventDescriptor(
            InputMapEventKind.JoypadAxis, Key.None, Key.None, 0, false, false, false, false,
            MouseButton.None, JoyButton.Invalid, JoyAxis.LeftX, 0.5f, -1));

        var adapter = new GodotInputMapAdapter(surface);

        var ex = Assert.Throws<InputMapConfigurationException>(() => adapter.CaptureFactory(InputActionCatalog.Actions));
        Assert.Contains("Attack", ex.Message);
    }

    [Fact]
    public void CaptureFactory_FailsOnOutOfRangeJoypadButton()
    {
        var surface = SeedSurface(BuildSet());
        surface.ReplaceEvents("Attack", new InputMapEventDescriptor(
            InputMapEventKind.JoypadButton, Key.None, Key.None, 0, false, false, false, false,
            MouseButton.None, (JoyButton)999, JoyAxis.Invalid, 0f, -1));

        var adapter = new GodotInputMapAdapter(surface);

        var ex = Assert.Throws<InputMapConfigurationException>(() => adapter.CaptureFactory(InputActionCatalog.Actions));
        Assert.Contains("Attack", ex.Message);
    }

    [Fact]
    public void CaptureFactory_FailsOnOutOfRangeJoypadAxis()
    {
        var surface = SeedSurface(BuildSet());
        surface.ReplaceEvents("Attack", new InputMapEventDescriptor(
            InputMapEventKind.JoypadAxis, Key.None, Key.None, 0, false, false, false, false,
            MouseButton.None, JoyButton.Invalid, (JoyAxis)999, -1f, -1));

        var adapter = new GodotInputMapAdapter(surface);

        var ex = Assert.Throws<InputMapConfigurationException>(() => adapter.CaptureFactory(InputActionCatalog.Actions));
        Assert.Contains("Attack", ex.Message);
    }

    [Fact]
    public void CaptureFactory_FailsOnOutOfRangeMouseButton()
    {
        var surface = SeedSurface(BuildSet());
        surface.ReplaceEvents("Attack", new InputMapEventDescriptor(
            InputMapEventKind.Mouse, Key.None, Key.None, 0, false, false, false, false,
            (MouseButton)999, JoyButton.Invalid, JoyAxis.Invalid, 0f, 0));

        var adapter = new GodotInputMapAdapter(surface);

        var ex = Assert.Throws<InputMapConfigurationException>(() => adapter.CaptureFactory(InputActionCatalog.Actions));
        Assert.Contains("Attack", ex.Message);
    }

    [Fact]
    public void CaptureFactory_FailsOnDuplicateEvent()
    {
        var surface = SeedSurface(BuildSet());
        var duplicate = InputMapEventDescriptor.Keyboard(Key.W, false, false, false, false);
        surface.ReplaceEvents("Attack", duplicate, duplicate);

        var adapter = new GodotInputMapAdapter(surface);

        var ex = Assert.Throws<InputMapConfigurationException>(() => adapter.CaptureFactory(InputActionCatalog.Actions));
        Assert.Contains("Attack", ex.Message);
    }

    [Fact]
    public void CaptureFactory_FailsOnDuplicateBindingsAfterDeviceDiscarding()
    {
        var surface = SeedSurface(BuildSet());
        var first = new InputMapEventDescriptor(
            InputMapEventKind.JoypadButton, Key.None, Key.None, 0, false, false, false, false,
            MouseButton.None, JoyButton.A, JoyAxis.Invalid, 0f, 0);
        var second = new InputMapEventDescriptor(
            InputMapEventKind.JoypadButton, Key.None, Key.None, 0, false, false, false, false,
            MouseButton.None, JoyButton.A, JoyAxis.Invalid, 0f, 1);
        surface.ReplaceEvents("Attack", first, second);

        var adapter = new GodotInputMapAdapter(surface);

        var ex = Assert.Throws<InputMapConfigurationException>(() => adapter.CaptureFactory(InputActionCatalog.Actions));
        Assert.Contains("Attack", ex.Message);
    }

    [Fact]
    public void Replace_PublishesNextMapAndPreservesEverythingElse()
    {
        var previous = BuildSet();
        var next = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.E, true, false, false, false)]),
            ("MoveUp", [new InputBinding.Mouse(MouseButton.WheelDown, false, false, false, false)]));

        var surface = SeedSurface(previous);
        var adapter = new GodotInputMapAdapter(surface);

        adapter.Replace(previous, next);

        AssertMapEquals(surface, next);
        Assert.Equal(
            [InputMapEventDescriptor.Keyboard(Key.Enter, false, false, false, false)],
            surface.Events(NonCatalogAction));
        Assert.Equal(0.5f, surface.GetActionDeadzone("Attack"));
        Assert.Equal(0.5f, surface.GetActionDeadzone(NonCatalogAction));
    }

    [Fact]
    public void Replace_PreservesPerActionEventOrder()
    {
        var previous = BuildSet();
        var next = BuildSet(
            ("Attack",
            [
                new InputBinding.Keyboard(Key.W, false, false, false, false),
                new InputBinding.Keyboard(Key.W, false, true, false, false),
                new InputBinding.Mouse(MouseButton.WheelUp, false, false, false, false),
                new InputBinding.JoypadButton(JoyButton.B)
            ]));

        var surface = SeedSurface(previous);
        var adapter = new GodotInputMapAdapter(surface);

        adapter.Replace(previous, next);

        Assert.Equal(
            [
                InputMapEventDescriptor.Keyboard(Key.W, false, false, false, false),
                InputMapEventDescriptor.Keyboard(Key.W, false, true, false, false),
                InputMapEventDescriptor.Mouse(MouseButton.WheelUp, false, false, false, false),
                InputMapEventDescriptor.JoypadButton(JoyButton.B)
            ],
            surface.Events("Attack"));
    }

    [Fact]
    public void Replace_RestoresPreviousMapWhenAddFailsMidReplacement()
    {
        var previous = BuildSet();
        var next = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)]));

        var surface = SeedSurface(previous);
        surface.AddFault = ("Attack", InputMapEventDescriptor.Keyboard(Key.E, false, false, false, false));
        var adapter = new GodotInputMapAdapter(surface);

        var ex = Assert.Throws<InputMapAdapterFailure>(() => adapter.Replace(previous, next));

        Assert.True(ex.RuntimeRestored);
        Assert.NotNull(ex.OriginalFailure);
        Assert.Null(ex.RestorationFailure);
        AssertMapEquals(surface, previous);
        Assert.Equal(
            [InputMapEventDescriptor.Keyboard(Key.Enter, false, false, false, false)],
            surface.Events(NonCatalogAction));
    }

    [Fact]
    public void Replace_RestoresPreviousMapWhenEraseFailsMidReplacement()
    {
        var previous = BuildSet();
        var next = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.Q, false, false, false, false)]),
            ("PickUp", [new InputBinding.Keyboard(Key.E, false, false, false, false)]));

        var surface = SeedSurface(previous);
        surface.EraseFaultOnce = "PickUp";
        var adapter = new GodotInputMapAdapter(surface);

        var ex = Assert.Throws<InputMapAdapterFailure>(() => adapter.Replace(previous, next));

        Assert.True(ex.RuntimeRestored);
        Assert.NotNull(ex.OriginalFailure);
        Assert.Null(ex.RestorationFailure);
        AssertMapEquals(surface, previous);
    }

    [Fact]
    public void Replace_ReportsRestorationFailureExplicitly()
    {
        var previous = BuildSet();
        var next = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)]));

        var surface = SeedSurface(previous);
        surface.AddFault = ("Attack", InputMapEventDescriptor.Keyboard(Key.E, false, false, false, false));
        surface.EraseFailAfter = InputActionCatalog.Actions.ToList().FindIndex(a => a.Name == "Attack") + 1;
        var adapter = new GodotInputMapAdapter(surface);

        var ex = Assert.Throws<InputMapAdapterFailure>(() => adapter.Replace(previous, next));

        Assert.False(ex.RuntimeRestored);
        Assert.NotNull(ex.OriginalFailure);
        Assert.NotNull(ex.RestorationFailure);
        Assert.Empty(surface.Events("Attack"));
        Assert.Equal(
            [InputMapEventDescriptor.Keyboard(Key.A, false, false, false, false)],
            surface.Events("MoveUp"));
    }

    [Fact]
    public void Replace_ValidatesAllNextBindingsBeforeAnyMutation()
    {
        var previous = BuildSet();
        var next = BuildSet(
            ("ToggleFullscreen", [new InputBinding.Keyboard(Key.None, false, false, false, false)]));

        var surface = SeedSurface(previous);
        var adapter = new GodotInputMapAdapter(surface);

        Assert.Throws<InputMapConfigurationException>(() => adapter.Replace(previous, next));

        Assert.Equal(0, surface.EraseCalls);
        Assert.Equal(0, surface.AddCalls);
        AssertMapEquals(surface, previous);
    }

    [Fact]
    public void Replace_RejectsDuplicateNextBindingBeforeAnyMutation()
    {
        var previous = BuildSet();
        var next = BuildSet(
            ("Attack",
            [
                new InputBinding.Keyboard(Key.W, false, false, false, false),
                new InputBinding.Keyboard(Key.W, false, false, false, false)
            ]));

        var surface = SeedSurface(previous);
        var adapter = new GodotInputMapAdapter(surface);

        Assert.Throws<InputMapConfigurationException>(() => adapter.Replace(previous, next));

        Assert.Equal(0, surface.EraseCalls);
        Assert.Equal(0, surface.AddCalls);
        AssertMapEquals(surface, previous);
    }

    [Fact]
    public void Replace_LeavesDeadzonesUnchanged()
    {
        var previous = BuildSet();
        var next = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)]));

        var surface = SeedSurface(previous);
        var adapter = new GodotInputMapAdapter(surface);

        adapter.Replace(previous, next);

        foreach (var action in InputActionCatalog.Actions)
            Assert.Equal(0.5f, surface.GetActionDeadzone(action.Name));
        Assert.Equal(0.5f, surface.GetActionDeadzone(NonCatalogAction));
    }

    private static InputBindingSet BuildSet(params (string Action, InputBinding[] Bindings)[] overrides)
    {
        var bindings = new Dictionary<string, IReadOnlyList<InputBinding>>();
        foreach (var action in InputActionCatalog.Actions)
        {
            InputBinding[] value = [new InputBinding.Keyboard(Key.A, false, false, false, false)];
            foreach (var (name, list) in overrides)
                if (name == action.Name)
                    value = list;

            bindings[action.Name] = value.AsReadOnly();
        }

        return new InputBindingSet(bindings);
    }

    private static FakeInputMapSurface SeedSurface(InputBindingSet set)
    {
        var surface = new FakeInputMapSurface();
        foreach (var action in InputActionCatalog.Actions)
            surface.Seed(action.Name, set.GetBindings(action.Name).Select(ToDescriptor).ToArray());

        surface.Seed(NonCatalogAction, InputMapEventDescriptor.Keyboard(Key.Enter, false, false, false, false));
        return surface;
    }

    private static void AssertMapEquals(FakeInputMapSurface surface, InputBindingSet expected)
    {
        foreach (var action in InputActionCatalog.Actions)
            Assert.Equal(
                expected.GetBindings(action.Name).Select(ToDescriptor).ToArray(),
                surface.Events(action.Name));
    }

    private static InputMapEventDescriptor ToDescriptor(InputBinding binding) => binding switch
    {
        InputBinding.Keyboard k => InputMapEventDescriptor.Keyboard(k.PhysicalKey, k.Ctrl, k.Shift, k.Alt, k.Meta),
        InputBinding.Mouse m => InputMapEventDescriptor.Mouse(m.Button, m.Ctrl, m.Shift, m.Alt, m.Meta),
        InputBinding.JoypadButton j => InputMapEventDescriptor.JoypadButton(j.Button),
        InputBinding.JoypadAxis a => InputMapEventDescriptor.JoypadAxis(a.Axis, a.Direction),
        _ => throw new InvalidOperationException($"Unsupported binding: {binding}")
    };

    private static InputMapEventDescriptor KeyboardDescriptor(Key physical, Key logical, uint unicode) =>
        new(InputMapEventKind.Keyboard, physical, logical, unicode, false, false, false, false,
            MouseButton.None, JoyButton.Invalid, JoyAxis.Invalid, 0f, 0);

    private sealed class FakeInputMapSurface : IInputMapSurface
    {
        private readonly Dictionary<string, List<InputMapEventDescriptor>> _events = new();
        private readonly Dictionary<string, float> _deadzones = new();

        public int EraseCalls { get; private set; }
        public int AddCalls { get; private set; }
        public string? EraseFaultOnce { get; set; }
        public int? EraseFailAfter { get; set; }
        public (string Action, InputMapEventDescriptor Descriptor)? AddFault { get; set; }

        public void Seed(string action, params InputMapEventDescriptor[] descriptors)
        {
            _events[action] = new List<InputMapEventDescriptor>(descriptors);
            _deadzones[action] = 0.5f;
        }

        public void ReplaceEvents(string action, params InputMapEventDescriptor[] descriptors) =>
            _events[action] = new List<InputMapEventDescriptor>(descriptors);

        public void Remove(string action)
        {
            _events.Remove(action);
            _deadzones.Remove(action);
        }

        public List<InputMapEventDescriptor> Events(string action) => _events[action];

        public bool HasAction(string action) => _events.ContainsKey(action);

        public float GetActionDeadzone(string action) =>
            _deadzones.TryGetValue(action, out var value) ? value : throw new KeyNotFoundException(action);

        public IReadOnlyList<InputMapEventDescriptor> GetActionEvents(string action) =>
            _events.TryGetValue(action, out var list) ? list : throw new KeyNotFoundException(action);

        public void EraseActionEvents(string action)
        {
            EraseCalls++;
            if (EraseFailAfter is { } limit && EraseCalls > limit)
                throw new InvalidOperationException($"injected erase failure: {action}");
            if (EraseFaultOnce == action)
            {
                EraseFaultOnce = null;
                throw new InvalidOperationException($"injected erase failure: {action}");
            }

            _events[action] = new List<InputMapEventDescriptor>();
        }

        public void AddActionEvent(string action, InputMapEventDescriptor descriptor)
        {
            AddCalls++;
            if (AddFault is { } fault && fault.Action == action && fault.Descriptor == descriptor)
                throw new InvalidOperationException($"injected add failure: {action}");

            if (!_events.ContainsKey(action))
                throw new KeyNotFoundException(action);

            _events[action].Add(descriptor);
        }
    }
}
