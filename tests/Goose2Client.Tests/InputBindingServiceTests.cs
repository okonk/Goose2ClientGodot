using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Goose2Client.InputBindings;
using Godot;
using Xunit;

namespace Goose2Client.Tests;

public class InputBindingServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly string _filePath;
    private readonly InputBindingFileStore _store;
    private readonly InputBindingSet _factory;
    private readonly FakeAdapter _adapter;
    private readonly InputBindingService _service;

    public InputBindingServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "input-binding-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _filePath = Path.Combine(_directory, "input-bindings.json");
        _store = new InputBindingFileStore(_filePath);
        _factory = BuildSet();
        _adapter = new FakeAdapter { Factory = _factory, FileProbe = _store };
        _service = new InputBindingService(_adapter, _store);
    }

    public void Dispose()
    {
        _store.FailBeforePublish = null;
        _store.FailDuringRollback = null;
        _store.FailDuringCleanup = null;
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
        }
    }

    private void SeedFile(InputBindingSet set)
    {
        var transaction = _store.Save(InputBindingJson.Serialize(set, _factory));
        Assert.True(transaction.Complete());
    }

    private void AssertObservableState(InputBindingSet active, byte[]? fileBytes, InputBindingSet? runtime)
    {
        AssertSetEquals(active, _service.Active);
        AssertSetEquals(_factory, _service.FactoryDefaults);
        Assert.Equal(fileBytes, _store.Read());
        if (runtime is null)
            Assert.Null(_adapter.Current);
        else
            AssertSetEquals(runtime, _adapter.Current!);
    }

    private static void AssertSetEquals(InputBindingSet expected, InputBindingSet actual)
    {
        foreach (var action in InputActionCatalog.Actions)
            Assert.Equal(expected.GetBindings(action.Name), actual.GetBindings(action.Name));
    }

    [Fact]
    public void Initialize_MissingFile_PublishesFactoryDefaultsAndCreatesNoFile()
    {
        var result = _service.Initialize();

        Assert.True(result.Success);
        Assert.Null(result.Warning);
        Assert.Equal(["capture"], _adapter.Operations);
        AssertObservableState(_factory, null, null);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Initialize_PartialOverrides_ResolveMissingActionsFromFactoryAndReplaceRuntime()
    {
        var resolved = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.E, true, false, false, false)]));
        SeedFile(resolved);
        var expectedBytes = _store.Read()!;

        var result = _service.Initialize();

        Assert.True(result.Success);
        Assert.Null(result.Warning);
        Assert.Equal(
            [new InputBinding.Keyboard(Key.E, true, false, false, false)],
            _service.Active.GetBindings("Attack"));
        Assert.Equal(_factory.GetBindings("MoveUp"), _service.Active.GetBindings("MoveUp"));
        Assert.Equal(["capture", "replace"], _adapter.Operations);
        AssertObservableState(resolved, expectedBytes, resolved);
    }

    [Fact]
    public void Initialize_ValidOverrides_ReplacesRuntimeMapIncludingUnboundActionAndLeavesFileUntouched()
    {
        var resolved = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.B, false, false, false, false)]),
            ("PickUp", []));
        SeedFile(resolved);
        var expectedBytes = _store.Read()!;

        var result = _service.Initialize();

        Assert.True(result.Success);
        Assert.Null(result.Warning);
        Assert.Equal(["capture", "replace"], _adapter.Operations);
        Assert.Empty(_service.Active.GetBindings("PickUp"));
        AssertObservableState(resolved, expectedBytes, resolved);
    }

    [Fact]
    public void Initialize_OverridesIdenticalToFactory_SkipsReplace()
    {
        SeedFile(_factory);
        var bytes = _store.Read()!;

        var result = _service.Initialize();

        Assert.True(result.Success);
        Assert.Null(result.Warning);
        Assert.Equal(["capture"], _adapter.Operations);
        AssertObservableState(_factory, bytes, null);
    }

    [Fact]
    public void Initialize_ReplaceFailure_RuntimeRestored_FallsBackToFactoryWithWarning()
    {
        SeedFile(BuildSet(("Attack", [new InputBinding.Keyboard(Key.B, false, false, false, false)])));
        var bytes = _store.Read()!;

        _adapter.FailReplace = true;
        _adapter.RuntimeRestored = true;
        var result = _service.Initialize();

        Assert.True(result.Success);
        Assert.NotNull(result.Warning);
        Assert.Equal(["capture", "replace"], _adapter.Operations);
        AssertObservableState(_factory, bytes, _factory);
    }

    [Fact]
    public void Initialize_ReplaceFailure_RuntimeNotRestored_RethrowsAndLeavesServiceUninitialized()
    {
        SeedFile(BuildSet(("Attack", [new InputBinding.Keyboard(Key.B, false, false, false, false)])));
        var bytes = _store.Read()!;

        _adapter.FailReplace = true;
        _adapter.RuntimeRestored = false;

        Assert.Throws<InputMapAdapterFailure>(() => _service.Initialize());

        Assert.Equal(["capture", "replace"], _adapter.Operations);
        Assert.False(_service.IsInitialized);
        Assert.Throws<InvalidOperationException>(() => _service.Active);
        Assert.Equal(bytes, _store.Read());
    }

    [Fact]
    public void Initialize_EmptyOverrideList_RemainsUnbound()
    {
        SeedFile(BuildSet(("Attack", [])));

        var result = _service.Initialize();

        Assert.True(result.Success);
        Assert.Null(result.Warning);
        Assert.Empty(_service.Active.GetBindings("Attack"));
        Assert.Equal(_factory.GetBindings("MoveUp"), _service.Active.GetBindings("MoveUp"));
    }

    [Fact]
    public void Initialize_InvalidFile_PublishesFactoryWithOneWarningAndLeavesBytesUntouched()
    {
        var bytes = new byte[] { 0x7B, 0xFF, 0x00, 0x21 };
        File.WriteAllBytes(_filePath, bytes);

        var result = _service.Initialize();

        Assert.True(result.Success);
        Assert.NotNull(result.Warning);
        Assert.Equal(["capture"], _adapter.Operations);
        AssertSetEquals(_factory, _service.Active);
        Assert.Equal(bytes, _store.Read());
        Assert.Null(_adapter.Current);
    }

    [Fact]
    public void Apply_Success_PublishesFileBeforeAdapterReplacement()
    {
        _service.Initialize();
        var candidate = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)]));
        var expectedBytes = InputBindingJson.Serialize(candidate, _factory);

        var result = _service.Apply(BuildDraft(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)])));

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Null(result.Warning);
        Assert.Null(result.Recovery);
        Assert.Equal(["capture", "replace"], _adapter.Operations);
        Assert.Equal(expectedBytes, _adapter.FileBytesAtReplace);
        AssertObservableState(candidate, expectedBytes, candidate);
    }

    [Fact]
    public void Apply_Success_WithExistingFile_UpdatesFileRuntimeAndActiveTogether()
    {
        SeedFile(BuildSet(("Attack", [new InputBinding.Keyboard(Key.B, false, false, false, false)])));
        _service.Initialize();
        var candidate = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)]));
        var expectedBytes = InputBindingJson.Serialize(candidate, _factory);

        var result = _service.Apply(BuildDraft(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)])));

        Assert.True(result.Success);
        Assert.Null(result.Recovery);
        Assert.Equal(expectedBytes, _adapter.FileBytesAtReplace);
        AssertObservableState(candidate, expectedBytes, candidate);
        Assert.Equal(new[] { _filePath }, Directory.GetFiles(_directory));
    }

    [Fact]
    public void Apply_InvalidDraft_FailsWithoutTouchingFileOrRuntime()
    {
        _service.Initialize();
        var draft = BuildDraft(("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)]));
        draft.Remove("MoveUp");

        var result = _service.Apply(draft);

        Assert.False(result.Success);
        Assert.Contains("MoveUp", result.Error);
        Assert.Null(result.Recovery);
        Assert.Equal(["capture"], _adapter.Operations);
        AssertObservableState(_factory, null, null);
    }

    [Fact]
    public void Apply_PublishFailure_NeverReplacesRuntimeAndLeavesNoFile()
    {
        _service.Initialize();
        _store.FailBeforePublish = _ => throw new IOException("injected publish failure");

        var result = _service.Apply(BuildDraft(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)])));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Null(result.Recovery);
        Assert.Equal(["capture"], _adapter.Operations);
        AssertObservableState(_factory, null, null);
    }

    [Fact]
    public void Apply_AdapterFailure_RollsFileBackAndLeavesStateUnchanged()
    {
        var initial = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.B, false, false, false, false)]));
        SeedFile(initial);
        _service.Initialize();
        var initialBytes = _store.Read()!;

        _adapter.FailReplace = true;
        _adapter.RuntimeRestored = true;
        var result = _service.Apply(BuildDraft(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)])));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Null(result.Recovery);
        Assert.Equal(["capture", "replace", "replace"], _adapter.Operations);
        AssertObservableState(initial, initialBytes, initial);
    }

    [Fact]
    public void Apply_UntypedAdapterFailure_RollsFileBackAndReportsRuntimeRecoveryRequired()
    {
        var initial = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.B, false, false, false, false)]));
        SeedFile(initial);
        _service.Initialize();
        var initialBytes = _store.Read()!;
        var injected = new InvalidOperationException("injected untyped adapter failure");

        _adapter.ReplaceThrows = injected;
        var result = _service.Apply(BuildDraft(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)])));

        Assert.False(result.Success);
        Assert.NotNull(result.Recovery);
        var recovery = result.Recovery!;
        Assert.True(recovery.RuntimeNeedsRecovery);
        Assert.False(recovery.FileNeedsRecovery);
        Assert.Same(injected, recovery.OriginalFailure);
        Assert.Null(recovery.RuntimeRestorationFailure);
        Assert.Null(recovery.FileRollbackFailure);
        AssertObservableState(initial, initialBytes, initial);
    }

    [Fact]
    public void Apply_AdapterRestorationFailure_ReturnsRecoveryRequiredEvenWhenFileRollsBack()
    {
        var initial = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.B, false, false, false, false)]));
        SeedFile(initial);
        _service.Initialize();
        var initialBytes = _store.Read()!;
        var partial = BuildSet(("Attack", []));

        _adapter.FailReplace = true;
        _adapter.RuntimeRestored = false;
        _adapter.RestorationFailure = new InvalidOperationException("injected restoration failure");
        _adapter.PartialRuntime = partial;
        var result = _service.Apply(BuildDraft(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)])));

        Assert.False(result.Success);
        Assert.NotNull(result.Recovery);
        var recovery = result.Recovery!;
        Assert.True(recovery.RuntimeNeedsRecovery);
        Assert.False(recovery.FileNeedsRecovery);
        Assert.Contains("injected adapter failure", recovery.OriginalFailure.Message);
        Assert.Contains("injected restoration failure", recovery.RuntimeRestorationFailure!.Message);
        Assert.Null(recovery.FileRollbackFailure);
        AssertObservableState(initial, initialBytes, partial);
    }

    [Fact]
    public void Apply_FileRollbackFailure_ReportsFileRecoveryWithoutClaimingUnchangedState()
    {
        var initial = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.B, false, false, false, false)]));
        SeedFile(initial);
        _service.Initialize();
        var candidate = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)]));
        var publishedBytes = InputBindingJson.Serialize(candidate, _factory);

        _adapter.FailReplace = true;
        _adapter.RuntimeRestored = true;
        _store.FailDuringRollback = _ => throw new IOException("injected rollback failure");
        var result = _service.Apply(BuildDraft(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)])));

        Assert.False(result.Success);
        Assert.NotNull(result.Recovery);
        var recovery = result.Recovery!;
        Assert.False(recovery.RuntimeNeedsRecovery);
        Assert.True(recovery.FileNeedsRecovery);
        Assert.NotNull(recovery.FileRollbackFailure);
        Assert.Equal(publishedBytes, _store.Read());
        AssertSetEquals(initial, _service.Active);
        AssertSetEquals(initial, _adapter.Current!);
    }

    [Fact]
    public void Apply_CombinedRestorationAndRollbackFailure_ReportsBothDimensions()
    {
        var initial = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.B, false, false, false, false)]));
        SeedFile(initial);
        _service.Initialize();

        _adapter.FailReplace = true;
        _adapter.RuntimeRestored = false;
        _adapter.RestorationFailure = new InvalidOperationException("injected restoration failure");
        _store.FailDuringRollback = _ => throw new IOException("injected rollback failure");
        var result = _service.Apply(BuildDraft(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)])));

        Assert.False(result.Success);
        Assert.NotNull(result.Recovery);
        var recovery = result.Recovery!;
        Assert.True(recovery.RuntimeNeedsRecovery);
        Assert.True(recovery.FileNeedsRecovery);
        Assert.Contains("injected adapter failure", recovery.OriginalFailure.Message);
        Assert.Contains("injected restoration failure", recovery.RuntimeRestorationFailure!.Message);
        Assert.NotNull(recovery.FileRollbackFailure);
        AssertSetEquals(initial, _service.Active);
    }

    [Fact]
    public void Apply_CleanupFailureAfterSuccess_KeepsPublishedStateAndReturnsSuccessWithWarning()
    {
        var initial = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.B, false, false, false, false)]));
        SeedFile(initial);
        _service.Initialize();
        var candidate = BuildSet(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)]));
        var expectedBytes = InputBindingJson.Serialize(candidate, _factory);

        _store.FailDuringCleanup = _ => throw new IOException("injected cleanup failure");
        var result = _service.Apply(BuildDraft(
            ("Attack", [new InputBinding.Keyboard(Key.E, false, false, false, false)])));

        Assert.True(result.Success);
        Assert.NotNull(result.Warning);
        Assert.Null(result.Recovery);
        AssertObservableState(candidate, expectedBytes, candidate);
    }

    [Fact]
    public void FactoryAndActive_AreImmutableUnderRepeatedAccess()
    {
        _service.Initialize();
        var draft = BuildDraft(
            ("Attack",
            [
                new InputBinding.Keyboard(Key.E, false, false, false, false),
                new InputBinding.Keyboard(Key.E, false, false, false, false),
                new InputBinding.Mouse(MouseButton.WheelUp, false, false, false, false)
            ]));
        var draftAttack = (List<InputBinding>)draft["Attack"];

        var result = _service.Apply(draft);
        Assert.True(result.Success);

        Assert.Equal(3, draftAttack.Count);
        var activeAttack = _service.Active.GetBindings("Attack");
        Assert.Equal(2, activeAttack.Count);
        Assert.Throws<NotSupportedException>(
            () => ((IList<InputBinding>)activeAttack).Add(new InputBinding.Keyboard(Key.Z, false, false, false, false)));

        draftAttack.Add(new InputBinding.Keyboard(Key.Z, false, false, false, false));
        Assert.Equal(2, _service.Active.GetBindings("Attack").Count);
        AssertSetEquals(_factory, _service.FactoryDefaults);
        Assert.Throws<NotSupportedException>(
            () => ((IList<InputBinding>)_service.FactoryDefaults.GetBindings("Attack"))
                .Add(new InputBinding.Keyboard(Key.Z, false, false, false, false)));
    }

    [Fact]
    public void Initialize_CannotRunTwice()
    {
        _service.Initialize();

        Assert.Throws<InvalidOperationException>(() => _service.Initialize());
    }

    [Fact]
    public void Apply_BeforeInitialize_FailsClearly()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _service.Apply(BuildDraft()));
        Assert.Contains("Initialize", ex.Message);
        Assert.Throws<InvalidOperationException>(() => _service.Active);
        Assert.Throws<InvalidOperationException>(() => _service.FactoryDefaults);
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

    private static Dictionary<string, IReadOnlyList<InputBinding>> BuildDraft(params (string Action, InputBinding[] Bindings)[] overrides)
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
        public byte[]? FileBytesAtReplace { get; private set; }
        public InputBindingFileStore? FileProbe { get; set; }
        public bool FailReplace { get; set; }
        public bool RuntimeRestored { get; set; } = true;
        public Exception? RestorationFailure { get; set; }
        public Exception? ReplaceThrows { get; set; }
        public InputBindingSet? PartialRuntime { get; set; }

        public InputBindingSet CaptureFactory(IReadOnlyList<InputActionDefinition> catalog)
        {
            Operations.Add("capture");
            return Factory!;
        }

        public void Replace(InputBindingSet previous, InputBindingSet next)
        {
            Operations.Add("replace");
            if (FileProbe is not null)
                FileBytesAtReplace = FileProbe.Read();

            if (ReplaceThrows is not null)
                throw ReplaceThrows;

            if (FailReplace)
            {
                Current = PartialRuntime ?? previous;
                throw new InputMapAdapterFailure(
                    RuntimeRestored,
                    new InvalidOperationException("injected adapter failure"),
                    RestorationFailure);
            }

            Current = next;
        }
    }
}
