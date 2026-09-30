using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Goose2Client.InputBindings;
using Godot;
using Xunit;

namespace Goose2Client.Tests;

public class InputBindingModelTests
{
    [Fact]
    public void KeyboardRecords_CompareStructurally()
    {
        var a = new InputBinding.Keyboard(Key.W, false, true, false, false);
        var b = new InputBinding.Keyboard(Key.W, false, true, false, false);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        Assert.NotEqual(a, new InputBinding.Keyboard(Key.A, false, true, false, false));
        Assert.NotEqual(a, new InputBinding.Keyboard(Key.W, true, true, false, false));
        Assert.NotEqual(a, new InputBinding.Keyboard(Key.W, false, false, false, false));
        Assert.NotEqual(a, new InputBinding.Keyboard(Key.W, false, true, true, false));
        Assert.NotEqual(a, new InputBinding.Keyboard(Key.W, false, true, false, true));
    }

    [Fact]
    public void MouseRecords_CompareStructurally()
    {
        var a = new InputBinding.Mouse(MouseButton.WheelUp, true, false, false, false);
        var b = new InputBinding.Mouse(MouseButton.WheelUp, true, false, false, false);
        Assert.Equal(a, b);

        Assert.NotEqual(a, new InputBinding.Mouse(MouseButton.Middle, true, false, false, false));
        Assert.NotEqual(a, new InputBinding.Mouse(MouseButton.WheelUp, false, false, false, false));
        Assert.NotEqual(a, new InputBinding.Mouse(MouseButton.WheelUp, true, true, false, false));
        Assert.NotEqual(a, new InputBinding.Mouse(MouseButton.WheelUp, true, false, true, false));
        Assert.NotEqual(a, new InputBinding.Mouse(MouseButton.WheelUp, true, false, false, true));
    }

    [Fact]
    public void JoypadButtonRecords_CompareStructurally()
    {
        Assert.Equal(new InputBinding.JoypadButton(JoyButton.DpadUp), new InputBinding.JoypadButton(JoyButton.DpadUp));
        Assert.NotEqual(new InputBinding.JoypadButton(JoyButton.DpadUp), new InputBinding.JoypadButton(JoyButton.DpadDown));
    }

    [Fact]
    public void JoypadAxisRecords_CompareStructurally_IncludingDirection()
    {
        Assert.Equal(new InputBinding.JoypadAxis(JoyAxis.LeftX, -1), new InputBinding.JoypadAxis(JoyAxis.LeftX, -1));
        Assert.NotEqual(new InputBinding.JoypadAxis(JoyAxis.LeftX, -1), new InputBinding.JoypadAxis(JoyAxis.LeftX, 1));
        Assert.NotEqual(new InputBinding.JoypadAxis(JoyAxis.LeftX, -1), new InputBinding.JoypadAxis(JoyAxis.RightX, -1));
    }

    [Fact]
    public void EditorNormalization_PreservesFirstSeenOrder_AndCollapsesDuplicates()
    {
        InputBinding a = new InputBinding.Keyboard(Key.W, false, false, false, false);
        InputBinding b = new InputBinding.Mouse(MouseButton.Middle, false, false, false, false);
        InputBinding c = new InputBinding.JoypadButton(JoyButton.A);
        InputBinding d = new InputBinding.Keyboard(Key.W, false, false, false, false);
        InputBinding e = new InputBinding.Mouse(MouseButton.Middle, false, false, false, false);

        var result = InputBindingRules.NormalizeForEditor(new[] { a, b, c, d, e });

        Assert.True(result.Success);
        Assert.Equal(new[] { a, b, c }, result.Bindings.ToArray());
    }

    [Fact]
    public void EditorNormalization_FailsOnAnInvalidBinding()
    {
        var result = InputBindingRules.NormalizeForEditor(
            new[] { new InputBinding.Keyboard(Key.A, false, false, false, false), new InputBinding.Keyboard(Key.None, false, false, false, false) });

        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Empty(result.Bindings);
    }

    [Fact]
    public void PersistedValidation_RejectsDuplicates_AndAcceptsUniqueValidLists()
    {
        InputBinding a = new InputBinding.Keyboard(Key.W, false, false, false, false);
        InputBinding b = new InputBinding.Keyboard(Key.W, false, false, false, false);

        var duplicate = InputBindingRules.ValidatePersisted(new[] { a, b });
        Assert.False(duplicate.Success);
        Assert.False(string.IsNullOrEmpty(duplicate.Error));

        var unique = InputBindingRules.ValidatePersisted(new[] { a, new InputBinding.JoypadAxis(JoyAxis.LeftY, 1) });
        Assert.True(unique.Success);
        Assert.Null(unique.Error);

        Assert.True(InputBindingRules.ValidatePersisted(Array.Empty<InputBinding>()).Success);
    }

    [Theory]
    [InlineData((int)Key.None)]
    [InlineData((int)Key.Ctrl)]
    [InlineData((int)Key.Shift)]
    [InlineData((int)Key.Alt)]
    [InlineData((int)Key.Meta)]
    [InlineData(unchecked((int)0x80000041))]
    [InlineData(unchecked((int)0x40000041))]
    [InlineData(unchecked((int)0x20000041))]
    [InlineData(unchecked((int)0x10000041))]
    public void Keyboard_InvalidPhysicalKeys_AreRejected(int key)
    {
        var result = InputBindingRules.NormalizeForEditor(new[] { new InputBinding.Keyboard((Key)key, true, false, false, false) });
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData((int)Key.A)]
    [InlineData((int)Key.W)]
    [InlineData((int)Key.Escape)]
    [InlineData((int)Key.F1)]
    [InlineData((int)Key.Space)]
    public void Keyboard_ValidPhysicalKeys_AreAccepted(int key)
    {
        var result = InputBindingRules.NormalizeForEditor(new[] { new InputBinding.Keyboard((Key)key, true, false, false, false) });
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData((int)MouseButton.None)]
    [InlineData((int)MouseButton.Left)]
    [InlineData((int)MouseButton.Right)]
    public void Mouse_ReservedButtons_AreRejected(int button)
    {
        var result = InputBindingRules.NormalizeForEditor(new[] { new InputBinding.Mouse((MouseButton)button, false, false, false, false) });
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData((int)MouseButton.Middle)]
    [InlineData((int)MouseButton.WheelUp)]
    [InlineData((int)MouseButton.WheelDown)]
    [InlineData((int)MouseButton.WheelLeft)]
    [InlineData((int)MouseButton.WheelRight)]
    [InlineData((int)MouseButton.Xbutton1)]
    [InlineData((int)MouseButton.Xbutton2)]
    public void Mouse_BindableButtons_AreAccepted(int button)
    {
        var result = InputBindingRules.NormalizeForEditor(new[] { new InputBinding.Mouse((MouseButton)button, false, false, false, false) });
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData((int)JoyButton.Invalid)]
    [InlineData((int)JoyButton.Max)]
    public void JoypadButton_InvalidOrMaxButtons_AreRejected(int button)
    {
        var result = InputBindingRules.NormalizeForEditor(new[] { new InputBinding.JoypadButton((JoyButton)button) });
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData((int)JoyButton.A)]
    [InlineData((int)JoyButton.DpadUp)]
    [InlineData((int)JoyButton.LeftShoulder)]
    [InlineData((int)JoyButton.SdlMax)]
    public void JoypadButton_ValidButtons_AreAccepted(int button)
    {
        var result = InputBindingRules.NormalizeForEditor(new[] { new InputBinding.JoypadButton((JoyButton)button) });
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData((int)JoyAxis.Invalid)]
    [InlineData((int)JoyAxis.Max)]
    public void JoypadAxis_InvalidOrMaxAxes_AreRejected(int axis)
    {
        var result = InputBindingRules.NormalizeForEditor(new[] { new InputBinding.JoypadAxis((JoyAxis)axis, 1) });
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData((int)JoyAxis.LeftX)]
    [InlineData((int)JoyAxis.LeftY)]
    [InlineData((int)JoyAxis.RightX)]
    [InlineData((int)JoyAxis.RightY)]
    [InlineData((int)JoyAxis.TriggerLeft)]
    [InlineData((int)JoyAxis.TriggerRight)]
    [InlineData((int)JoyAxis.SdlMax)]
    public void JoypadAxis_ValidAxes_AreAccepted(int axis)
    {
        var result = InputBindingRules.NormalizeForEditor(new[] { new InputBinding.JoypadAxis((JoyAxis)axis, -1) });
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(-2)]
    [InlineData(100)]
    public void JoypadAxis_OnlyDirectionsMinusOneAndPlusOne_AreAccepted(int direction)
    {
        var result = InputBindingRules.NormalizeForEditor(new[] { new InputBinding.JoypadAxis(JoyAxis.LeftX, direction) });
        Assert.Equal(direction is -1 or 1, result.Success);
    }

    [Fact]
    public void BothEntryPoints_RejectNullElements()
    {
        var input = new List<InputBinding> { new InputBinding.Keyboard(Key.W, false, false, false, false), null! };

        var editor = InputBindingRules.NormalizeForEditor(input);
        Assert.False(editor.Success);
        Assert.Equal("Binding must not be null.", editor.Error);

        var persisted = InputBindingRules.ValidatePersisted(input);
        Assert.False(persisted.Success);
        Assert.Equal("Binding must not be null.", persisted.Error);
    }

    [Fact]
    public void InputBindingSet_DeepCopiesLists_SourceMutationCannotMutateSnapshot()
    {
        var source = new List<InputBinding>
        {
            new InputBinding.Keyboard(Key.W, false, false, false, false),
            new InputBinding.JoypadButton(JoyButton.A)
        };
        var bindings = CompleteSet(new Dictionary<string, IReadOnlyList<InputBinding>>
        {
            ["Attack"] = source
        });

        var snapshot = new InputBindingSet(bindings);

        source.Add(new InputBinding.Mouse(MouseButton.Middle, false, false, false, false));
        source.RemoveAt(0);

        var attack = snapshot.GetBindings("Attack");
        Assert.Equal(2, attack.Count);
        Assert.IsType<InputBinding.Keyboard>(attack[0]);
        Assert.IsType<InputBinding.JoypadButton>(attack[1]);
    }

    [Fact]
    public void InputBindingSet_RequiresEveryCatalogAction()
    {
        var bindings = CompleteSet();
        bindings.Remove("Attack");

        Assert.Throws<ArgumentException>(() => new InputBindingSet(bindings));
    }

    [Fact]
    public void InputBindingSet_RejectsUnknownActions()
    {
        var bindings = CompleteSet();
        bindings["Move"] = Array.Empty<InputBinding>();

        Assert.Throws<ArgumentException>(() => new InputBindingSet(bindings));
    }

    [Fact]
    public void InputBindingSet_LookupAndClone_AreDefensive()
    {
        var list = new List<InputBinding> { new InputBinding.Keyboard(Key.W, false, false, false, false) };
        var snapshot = new InputBindingSet(CompleteSet(new Dictionary<string, IReadOnlyList<InputBinding>>
        {
            ["Attack"] = list
        }));

        Assert.True(snapshot.TryGetBindings("Attack", out var attack));
        Assert.Single(attack);
        Assert.False(snapshot.TryGetBindings("Move", out _));
        Assert.Throws<KeyNotFoundException>(() => snapshot.GetBindings("Move"));

        list.Clear();
        Assert.Single(snapshot.GetBindings("Attack"));

        var clone = snapshot.Clone();
        Assert.NotSame(snapshot, clone);
        Assert.Equal(snapshot.GetBindings("Attack"), clone.GetBindings("Attack"));
        Assert.NotSame(snapshot.GetBindings("Attack"), clone.GetBindings("Attack"));
    }

    [Fact]
    public void InputBindingSet_RejectsDuplicateActionEntries()
    {
        var entries = CompleteSet()
            .Select(kv => new KeyValuePair<string, IReadOnlyList<InputBinding>>(kv.Key, kv.Value))
            .ToList();
        entries.Add(new KeyValuePair<string, IReadOnlyList<InputBinding>>("Attack", Array.Empty<InputBinding>()));

        Assert.Throws<ArgumentException>(() => new InputBindingSet(new KeyValueList(entries)));
    }

    private sealed class KeyValueList : IReadOnlyDictionary<string, IReadOnlyList<InputBinding>>
    {
        private readonly List<KeyValuePair<string, IReadOnlyList<InputBinding>>> _entries;

        public KeyValueList(List<KeyValuePair<string, IReadOnlyList<InputBinding>>> entries) => _entries = entries;

        public IReadOnlyList<InputBinding> this[string key] => _entries.First(e => e.Key == key).Value;
        public IEnumerable<string> Keys => _entries.Select(e => e.Key);
        public IEnumerable<IReadOnlyList<InputBinding>> Values => _entries.Select(e => e.Value);
        public int Count => _entries.Count;
        public bool ContainsKey(string key) => _entries.Any(e => e.Key == key);
        public bool TryGetValue(string key, out IReadOnlyList<InputBinding> value)
        {
            var match = _entries.FirstOrDefault(e => e.Key == key);
            value = match.Value;
            return match.Key != null;
        }

        public IEnumerator<KeyValuePair<string, IReadOnlyList<InputBinding>>> GetEnumerator() => _entries.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static Dictionary<string, IReadOnlyList<InputBinding>> CompleteSet(
        Dictionary<string, IReadOnlyList<InputBinding>>? overrides = null)
    {
        var result = new Dictionary<string, IReadOnlyList<InputBinding>>();
        foreach (var action in InputActionCatalog.Actions)
            result[action.Name] = Array.Empty<InputBinding>();

        if (overrides != null)
            foreach (var pair in overrides)
                result[pair.Key] = pair.Value;

        return result;
    }
}
