using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Goose2Client.InputBindings;
using Xunit;

namespace Goose2Client.Tests;

public class InputBindingSuppressionTests : IDisposable
{
    private readonly string _directory;
    private readonly InputBindingFileStore _store;
    private readonly InputBindingSet _factory;
    private readonly FakeAdapter _adapter;
    private readonly InputBindingService _service;
    private readonly FakeReleaseState _input;

    public InputBindingSuppressionTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "input-binding-suppression-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new InputBindingFileStore(Path.Combine(_directory, "input-bindings.json"));
        _factory = BuildSet();
        _adapter = new FakeAdapter { Factory = _factory };
        _service = new InputBindingService(_adapter, _store);
        _input = new FakeReleaseState();
        _service.Initialize();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void BeginSuppression_EmptiesAllCatalogListsButLeavesActiveUnchanged()
    {
        var lease = _service.BeginSuppression();

        Assert.True(_service.CaptureGateHeld);
        Assert.False(lease.IsRestored);
        foreach (var action in InputActionCatalog.Actions)
            Assert.Empty(_adapter.Current!.GetBindings(action.Name));
        AssertSetEquals(_factory, _service.Active);
        Assert.Null(_store.Read());
    }

    [Fact]
    public void BeginSuppression_SecondLeaseIsRejected()
    {
        _service.BeginSuppression();

        Assert.Throws<InvalidOperationException>(() => _service.BeginSuppression());
    }

    [Fact]
    public void BeginSuppression_BeforeInitialize_Throws()
    {
        var service = new InputBindingService(
            new FakeAdapter { Factory = BuildSet() },
            new InputBindingFileStore(Path.Combine(_directory, "other.json")));

        Assert.Throws<InvalidOperationException>(() => service.BeginSuppression());
    }

    [Fact]
    public void Restore_RepublishesExactActiveSnapshotExactlyOnce()
    {
        var lease = _service.BeginSuppression();
        var restores = 0;
        var gateReleases = 0;
        _service.SuppressionRestored += () => restores++;
        _service.CaptureGateReleased += () => gateReleases++;
        var operationsBefore = _adapter.Operations.Count;

        lease.RequestRestore(InputReleaseGate.Immediate);
        _service.ProcessSuppression(_input);

        AssertSetEquals(_factory, _adapter.Current!);
        AssertSetEquals(_factory, _service.Active);
        Assert.False(_service.CaptureGateHeld);
        Assert.True(lease.IsRestored);
        Assert.Equal(1, restores);
        Assert.Equal(1, gateReleases);
        Assert.Equal(operationsBefore + 1, _adapter.Operations.Count);

        lease.RequestRestore(InputReleaseGate.Immediate);
        _service.ProcessSuppression(_input);
        _service.ProcessSuppression(_input);

        Assert.Equal(operationsBefore + 1, _adapter.Operations.Count);
        Assert.Equal(1, restores);
        Assert.Equal(1, gateReleases);
    }

    [Fact]
    public void Restore_RepublishesActiveNotFactory_WhenActiveDiffers()
    {
        var candidate = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)]));
        var applied = _service.Apply(BuildDraft(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)])));
        Assert.True(applied.Success);

        var lease = _service.BeginSuppression();
        AssertSetEquals(candidate, _service.Active);
        lease.RequestRestore(InputReleaseGate.Immediate);
        _service.ProcessSuppression(_input);

        AssertSetEquals(candidate, _adapter.Current!);
        AssertSetEquals(candidate, _service.Active);
    }

    [Fact]
    public void Apply_IsRejectedWhileLeaseActiveAndSucceedsAfterRestore()
    {
        var lease = _service.BeginSuppression();
        var draft = BuildDraft(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)]));

        var rejected = _service.Apply(draft);
        Assert.False(rejected.Success);
        Assert.NotNull(rejected.Error);
        AssertSetEquals(EmptySet(), _adapter.Current!);
        Assert.Null(_store.Read());

        lease.RequestRestore(InputReleaseGate.Immediate);
        _service.ProcessSuppression(_input);

        var applied = _service.Apply(draft);
        Assert.True(applied.Success);
        AssertSetEquals(_service.Active, _adapter.Current!);
        Assert.NotNull(_store.Read());
    }

    [Fact]
    public void SuppressionRestored_FiresAfterRuntimeIsRestored()
    {
        var lease = _service.BeginSuppression();
        InputBindingSet? observed = null;
        _service.SuppressionRestored += () => observed = _adapter.Current;

        lease.RequestRestore(InputReleaseGate.Immediate);
        _service.ProcessSuppression(_input);

        AssertSetEquals(_factory, observed!);
    }

    [Fact]
    public void BeginSuppression_EraseFailure_RollsBackAndPublishesNoLease()
    {
        _adapter.FailReplace = true;
        _adapter.RuntimeRestored = true;

        Assert.Throws<InputMapAdapterFailure>(() => _service.BeginSuppression());

        Assert.False(_service.CaptureGateHeld);
        AssertSetEquals(_factory, _adapter.Current!);

        _service.ProcessSuppression(_input);
        AssertSetEquals(_factory, _adapter.Current!);
    }

    [Fact]
    public void RestoreFailure_LeaseRemainsServiceOwnedAndRetriesNextProcess()
    {
        var lease = _service.BeginSuppression();
        lease.RequestRestore(InputReleaseGate.Immediate);

        _adapter.FailReplace = true;
        _adapter.RuntimeRestored = true;
        _service.ProcessSuppression(_input);

        Assert.True(_service.CaptureGateHeld);
        Assert.False(lease.IsRestored);
        AssertSetEquals(EmptySet(), _adapter.Current!);

        _adapter.FailReplace = false;
        _service.ProcessSuppression(_input);

        Assert.False(_service.CaptureGateHeld);
        Assert.True(lease.IsRestored);
        AssertSetEquals(_factory, _adapter.Current!);
    }

    [Fact]
    public void RestoreFailure_StillRetriedAfterWindowSideLeaseReferenceIsGone()
    {
        var lease = _service.BeginSuppression();
        lease.RequestRestore(InputReleaseGate.Immediate);

        _adapter.FailReplace = true;
        _adapter.RuntimeRestored = true;
        _service.ProcessSuppression(_input);
        Assert.True(_service.CaptureGateHeld);

        lease = null;
        _adapter.FailReplace = false;
        _service.ProcessSuppression(_input);

        Assert.False(_service.CaptureGateHeld);
        AssertSetEquals(_factory, _adapter.Current!);
    }

    [Fact]
    public void HeldPhysicalKeyGate_DelaysRestoreUntilKeyIsReleased()
    {
        var lease = _service.BeginSuppression();
        _input.Keys.Add(Key.A);
        lease.RequestRestore(InputReleaseGate.ForPhysicalKey(Key.A));

        _service.ProcessSuppression(_input);
        Assert.True(_service.CaptureGateHeld);
        AssertSetEquals(EmptySet(), _adapter.Current!);

        _input.Keys.Remove(Key.A);
        _service.ProcessSuppression(_input);
        Assert.False(_service.CaptureGateHeld);
        AssertSetEquals(_factory, _adapter.Current!);
    }

    [Fact]
    public void HeldJoypadButtonGate_DelaysRestoreUntilButtonIsReleased()
    {
        var lease = _service.BeginSuppression();
        _input.JoyButtons.Add((1, JoyButton.A));
        lease.RequestRestore(InputReleaseGate.ForJoypadButton(1, JoyButton.A));

        _service.ProcessSuppression(_input);
        Assert.True(_service.CaptureGateHeld);

        _input.JoyButtons.Remove((1, JoyButton.A));
        _service.ProcessSuppression(_input);
        Assert.False(_service.CaptureGateHeld);
        AssertSetEquals(_factory, _adapter.Current!);
    }

    [Fact]
    public void JoypadAxisGate_DelaysRestoreUntilAxisIsNeutral()
    {
        var lease = _service.BeginSuppression();
        _input.Axes[(0, JoyAxis.LeftX)] = 0.9f;
        lease.RequestRestore(InputReleaseGate.ForJoypadAxis(0, JoyAxis.LeftX));

        _service.ProcessSuppression(_input);
        Assert.True(_service.CaptureGateHeld);

        _input.Axes[(0, JoyAxis.LeftX)] = -0.2f;
        _service.ProcessSuppression(_input);
        Assert.False(_service.CaptureGateHeld);
        AssertSetEquals(_factory, _adapter.Current!);
    }

    [Fact]
    public void NextFrameGate_RestoresOnNextServiceProcessTickAndCannotStrand()
    {
        var lease = _service.BeginSuppression();
        lease.RequestRestore(InputReleaseGate.NextFrame);

        _service.ProcessSuppression(_input);

        Assert.False(_service.CaptureGateHeld);
        Assert.True(lease.IsRestored);
        AssertSetEquals(_factory, _adapter.Current!);
    }

    [Fact]
    public void Suppression_NeverPublishesDraftOrFile()
    {
        var lease = _service.BeginSuppression();
        lease.RequestRestore(InputReleaseGate.Immediate);
        _service.ProcessSuppression(_input);

        Assert.Null(_store.Read());
        AssertSetEquals(_factory, _service.Active);
        AssertSetEquals(_factory, _adapter.Current!);
    }

    private static void AssertSetEquals(InputBindingSet expected, InputBindingSet actual)
    {
        foreach (var action in InputActionCatalog.Actions)
            Assert.Equal(expected.GetBindings(action.Name), actual.GetBindings(action.Name));
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

    private static InputBindingSet EmptySet()
    {
        var empty = new Dictionary<string, IReadOnlyList<InputBinding>>();
        foreach (var action in InputActionCatalog.Actions)
            empty[action.Name] = Array.Empty<InputBinding>();
        return new InputBindingSet(empty);
    }

    private static Dictionary<string, IReadOnlyList<InputBinding>> BuildDraft(
        params (string Action, InputBinding[] Bindings)[] overrides)
    {
        var draft = new Dictionary<string, IReadOnlyList<InputBinding>>();
        foreach (var action in InputActionCatalog.Actions)
        {
            var list = new List<InputBinding> { new InputBinding.Keyboard(Key.A, false, false, false, false) };
            foreach (var (name, bindings) in overrides)
                if (name == action.Name)
                    list = new List<InputBinding>(bindings);

            draft[action.Name] = list;
        }

        return draft;
    }

    private sealed class FakeAdapter : IInputMapAdapter
    {
        public List<string> Operations { get; } = new();
        public InputBindingSet? Factory { get; set; }
        public InputBindingSet? Current { get; private set; }
        public bool FailReplace { get; set; }
        public bool RuntimeRestored { get; set; } = true;

        public InputBindingSet CaptureFactory(IReadOnlyList<InputActionDefinition> catalog)
        {
            Operations.Add("capture");
            return Factory!;
        }

        public void Replace(InputBindingSet previous, InputBindingSet next)
        {
            Operations.Add("replace");
            if (FailReplace)
            {
                Current = previous;
                throw new InputMapAdapterFailure(
                    RuntimeRestored,
                    new InvalidOperationException("injected adapter failure"),
                    RuntimeRestored ? null : new InvalidOperationException("injected restoration failure"));
            }

            Current = next;
        }
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
