using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Goose2Client.InputBindings;
using Godot;
using Xunit;

namespace Goose2Client.Tests;

public class KeyBindingEditorStateTests : IDisposable
{
    private static readonly InputBinding[] AttackFactory =
    [
        new InputBinding.Keyboard(Key.E, true, false, false, false)
    ];

    private readonly string _directory;
    private readonly FakeAdapter _adapter;
    private readonly InputBindingService _service;

    public KeyBindingEditorStateTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "key-binding-editor-state-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _adapter = new FakeAdapter { Factory = BuildFactory() };
        _service = new InputBindingService(_adapter, new InputBindingFileStore(Path.Combine(_directory, "input-bindings.json")));
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
    public void Open_DeepCopiesActiveAndFactorySnapshots()
    {
        var editor = new KeyBindingEditorState(_service);

        Assert.False(editor.IsDirty);
        AssertDraft(editor, ("Attack", AttackFactory));

        editor.Add("Attack", new InputBinding.Keyboard(Key.B, false, false, false, false));
        editor.Remove("Attack", 1);
        editor.Remove("Attack", 0);

        AssertDraft(editor, ("Attack", []));
        Assert.Equal(AttackFactory, _service.Active.GetBindings("Attack"));
        Assert.Equal(AttackFactory, _service.FactoryDefaults.GetBindings("Attack"));
    }

    [Fact]
    public void Add_AppendsBindingAndMarksDraftDirty()
    {
        var editor = new KeyBindingEditorState(_service);
        var binding = new InputBinding.Keyboard(Key.B, false, false, false, false);

        Assert.True(editor.Add("MoveUp", binding));

        Assert.True(editor.IsDirty);
        AssertDraft(editor, ("Attack", AttackFactory), ("MoveUp", [binding]));
    }

    [Fact]
    public void Add_DuplicateBinding_CollapsesToSingleEntry()
    {
        var editor = new KeyBindingEditorState(_service);
        var binding = new InputBinding.Keyboard(Key.B, false, false, false, false);

        Assert.True(editor.Add("MoveUp", binding));
        Assert.True(editor.Add("MoveUp", binding));

        AssertDraft(editor, ("Attack", AttackFactory), ("MoveUp", [binding]));
    }

    [Fact]
    public void Add_InvalidBinding_IsRejectedAndLeavesDraftUnchanged()
    {
        var editor = new KeyBindingEditorState(_service);

        Assert.False(editor.Add("Attack", new InputBinding.Mouse(MouseButton.Left, false, false, false, false)));

        Assert.False(editor.IsDirty);
        AssertDraft(editor, ("Attack", AttackFactory));
    }

    [Fact]
    public void Replace_PreservesPosition()
    {
        var editor = new KeyBindingEditorState(_service);
        var b = new InputBinding.Keyboard(Key.B, false, false, false, false);
        var c = new InputBinding.Keyboard(Key.C, false, false, false, false);
        var d = new InputBinding.Keyboard(Key.D, false, false, false, false);
        var x = new InputBinding.Keyboard(Key.X, false, false, false, false);
        editor.Add("MoveUp", b);
        editor.Add("MoveUp", c);
        editor.Add("MoveUp", d);

        Assert.True(editor.Replace("MoveUp", 1, x));

        AssertDraft(editor, ("Attack", AttackFactory), ("MoveUp", [b, x, d]));
    }

    [Fact]
    public void Replace_DuplicateOfExistingBinding_CollapsesDeterministically()
    {
        var editor = new KeyBindingEditorState(_service);
        var b = new InputBinding.Keyboard(Key.B, false, false, false, false);
        var c = new InputBinding.Keyboard(Key.C, false, false, false, false);
        editor.Add("MoveUp", b);
        editor.Add("MoveUp", c);

        Assert.True(editor.Replace("MoveUp", 1, b));

        AssertDraft(editor, ("Attack", AttackFactory), ("MoveUp", [b]));
    }

    [Fact]
    public void RemoveLast_LeavesPresentEmptyActionList()
    {
        var editor = new KeyBindingEditorState(_service);

        Assert.True(editor.Remove("Attack", 0));

        Assert.True(editor.IsDirty);
        AssertDraft(editor, ("Attack", []));
        var row = Assert.Single(Rows(editor, "Attack"));
        Assert.Null(row.Binding);
        Assert.True(row.IsDirty);
    }

    [Fact]
    public void ResetAction_CopiesOnlyThatActionFromFactory()
    {
        var editor = new KeyBindingEditorState(_service);
        var b = new InputBinding.Keyboard(Key.B, false, false, false, false);
        var c = new InputBinding.Keyboard(Key.C, false, false, false, false);
        editor.Add("Attack", b);
        editor.Add("MoveUp", c);

        editor.ResetAction("Attack");

        AssertDraft(editor, ("Attack", AttackFactory), ("MoveUp", [c]));
    }

    [Fact]
    public void IsFactoryDefault_TracksDraftAgainstFactoryNotBaseline()
    {
        var editor = new KeyBindingEditorState(_service);
        Assert.True(editor.IsFactoryDefault("Attack"));

        editor.Add("Attack", new InputBinding.Keyboard(Key.B, false, false, false, false));
        Assert.False(editor.IsFactoryDefault("Attack"));

        editor.Apply();
        Assert.False(editor.IsDirty);
        Assert.False(editor.IsFactoryDefault("Attack"));

        editor.ResetAction("Attack");
        Assert.True(editor.IsFactoryDefault("Attack"));
    }

    [Fact]
    public void IsFactoryBinding_MatchesOnlyThatActionsFactoryBindings()
    {
        var editor = new KeyBindingEditorState(_service);

        Assert.True(editor.IsFactoryBinding("Attack", AttackFactory[0]));
        Assert.False(editor.IsFactoryBinding("Attack", new InputBinding.Keyboard(Key.B, false, false, false, false)));
        Assert.False(editor.IsFactoryBinding("MoveUp", AttackFactory[0]));
    }

    [Fact]
    public void ResetAll_CopiesEntireFactorySnapshot()
    {
        var editor = new KeyBindingEditorState(_service);
        var c = new InputBinding.Keyboard(Key.C, false, false, false, false);
        editor.Remove("Attack", 0);
        editor.Add("MoveUp", c);

        editor.ResetAll();

        Assert.False(editor.IsDirty);
        AssertDraft(editor, ("Attack", AttackFactory));
    }

    [Fact]
    public void ApplySuccess_EstablishesReturnedActiveSnapshotAsNewBaseline()
    {
        var editor = new KeyBindingEditorState(_service);
        var binding = new InputBinding.Keyboard(Key.B, false, false, false, false);
        editor.Add("MoveUp", binding);

        var result = editor.Apply();

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Null(editor.Error);
        Assert.False(editor.IsDirty);
        Assert.Equal(new[] { binding }, _service.Active.GetBindings("MoveUp"));
        AssertDraft(editor, ("Attack", AttackFactory), ("MoveUp", [binding]));

        editor.Add("MoveUp", new InputBinding.Keyboard(Key.C, false, false, false, false));
        editor.Cancel();

        Assert.False(editor.IsDirty);
        AssertDraft(editor, ("Attack", AttackFactory), ("MoveUp", [binding]));
    }

    [Fact]
    public void Apply_EmptyActionList_PersistsIntentionallyUnboundState()
    {
        var editor = new KeyBindingEditorState(_service);
        editor.Remove("Attack", 0);

        var result = editor.Apply();

        Assert.True(result.Success);
        Assert.Empty(_service.Active.GetBindings("Attack"));
        Assert.False(editor.IsDirty);
        AssertDraft(editor, ("Attack", []));
    }

    [Fact]
    public void ApplyFailure_RetainsDraftAndBaselineAndPublishesSafeError()
    {
        var editor = new KeyBindingEditorState(_service);
        var binding = new InputBinding.Keyboard(Key.B, false, false, false, false);
        editor.Add("MoveUp", binding);

        _adapter.FailReplace = true;
        _adapter.RuntimeRestored = true;
        var result = editor.Apply();

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(result.Error, editor.Error);
        Assert.True(editor.IsDirty);
        Assert.Empty(_service.Active.GetBindings("MoveUp"));
        AssertDraft(editor, ("Attack", AttackFactory), ("MoveUp", [binding]));

        editor.Add("MoveUp", new InputBinding.Keyboard(Key.C, false, false, false, false));
        editor.Cancel();

        Assert.False(editor.IsDirty);
        AssertDraft(editor, ("Attack", AttackFactory));
    }

    [Fact]
    public void Cancel_DiscardsEditsAndReclonesCurrentServiceActive()
    {
        var editor = new KeyBindingEditorState(_service);
        var binding = new InputBinding.Keyboard(Key.B, false, false, false, false);
        editor.Add("MoveUp", binding);

        editor.Cancel();

        Assert.False(editor.IsDirty);
        Assert.Empty(_service.Active.GetBindings("MoveUp"));
        AssertDraft(editor, ("Attack", AttackFactory));

        editor.Add("MoveUp", binding);
        Assert.True(editor.IsDirty);
        editor.Cancel();
        Assert.False(editor.IsDirty);
        AssertDraft(editor, ("Attack", AttackFactory));
    }

    [Theory]
    [InlineData("hot")]
    [InlineData("HOT")]
    [InlineData("Hotbar")]
    public void Search_MatchesCategoryCaseInsensitively(string term)
    {
        var editor = new KeyBindingEditorState(_service);
        editor.Search = term;

        var group = Assert.Single(editor.Groups);
        Assert.Equal("Hotbar", group.Category);
        Assert.Equal(12, group.Rows.Count);
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("INVENTORY")]
    public void Search_MatchesActionLabelCaseInsensitively(string term)
    {
        var editor = new KeyBindingEditorState(_service);
        editor.Search = term;

        var group = Assert.Single(editor.Groups);
        Assert.Equal("Windows", group.Category);
        var row = Assert.Single(group.Rows);
        Assert.Equal("ToggleInventory", row.Action.Name);
    }

    [Fact]
    public void Search_MatchingMultipleCategories_PreservesCatalogOrder()
    {
        var editor = new KeyBindingEditorState(_service);
        editor.Search = "toggle";

        Assert.Equal(new[] { "Combat & Interaction", "System" }, editor.Groups.Select(g => g.Category));
        Assert.Equal(new[] { "ToggleMount" }, editor.Groups[0].Rows.Select(r => r.Action.Name));
        Assert.Equal(new[] { "ToggleFullscreen" }, editor.Groups[1].Rows.Select(r => r.Action.Name));
    }

    [Fact]
    public void Search_WithNoMatches_OmitsAllGroups()
    {
        var editor = new KeyBindingEditorState(_service);
        editor.Search = "zzz";

        Assert.Empty(editor.Groups);
        Assert.Equal("No actions match \"zzz\".", editor.StatusText);
    }

    [Fact]
    public void Conflicts_AreProjectedOntoBothAffectedRows_WithoutBlockingApply()
    {
        var editor = new KeyBindingEditorState(_service);
        var binding = new InputBinding.Keyboard(Key.Up, false, false, false, false);
        editor.Add("MoveUp", binding);
        editor.Add("TargetDown", binding);

        var moveRow = Row(editor, "MoveUp", 0);
        var targetRow = Row(editor, "TargetDown", 0);

        var conflict = Assert.Single(moveRow.Conflicts);
        Assert.Equal("MoveUp", conflict.FirstAction);
        Assert.Equal("TargetDown", conflict.SecondAction);
        Assert.Equal(new[] { binding }, conflict.SharedBindings);
        Assert.Same(conflict, Assert.Single(targetRow.Conflicts));
        Assert.Empty(Row(editor, "TargetUp", 0).Conflicts);

        var result = editor.Apply();

        Assert.True(result.Success);
        Assert.False(editor.IsDirty);
        Assert.Single(Row(editor, "MoveUp", 0).Conflicts);
        Assert.Single(Row(editor, "TargetDown", 0).Conflicts);
    }

    [Fact]
    public void StartupWarning_RemainsVisibleUntilSuccessfulApply()
    {
        const string warning = "Input bindings file could not be loaded and factory defaults are active: bad json";
        var editor = new KeyBindingEditorState(_service, warning);
        var binding = new InputBinding.Keyboard(Key.B, false, false, false, false);

        Assert.Equal(warning, editor.StartupWarning);
        Assert.Equal(warning, editor.StatusText);

        editor.Add("MoveUp", binding);
        Assert.Equal(warning, editor.StatusText);

        editor.Cancel();
        Assert.Equal(warning, editor.StartupWarning);
        Assert.Equal(warning, editor.StatusText);

        editor.Add("MoveUp", binding);
        Assert.True(editor.Apply().Success);

        Assert.Null(editor.StartupWarning);
        Assert.NotEqual(warning, editor.StatusText);
    }

    [Fact]
    public void StatusText_PrefersErrorOverDirtyState()
    {
        var editor = new KeyBindingEditorState(_service);
        var binding = new InputBinding.Keyboard(Key.B, false, false, false, false);

        Assert.Equal("No unsaved changes.", editor.StatusText);

        editor.Add("MoveUp", binding);
        Assert.Equal("Unsaved changes.", editor.StatusText);

        _adapter.FailReplace = true;
        _adapter.RuntimeRestored = true;
        var result = editor.Apply();
        Assert.False(result.Success);
        Assert.Equal(result.Error, editor.StatusText);

        editor.Cancel();
        Assert.Equal("No unsaved changes.", editor.StatusText);
    }

    [Fact]
    public void Edits_NeverLeakIntoServiceSnapshots()
    {
        var editor = new KeyBindingEditorState(_service);
        var activeBefore = SnapshotOf(_service.Active);
        var factoryBefore = SnapshotOf(_service.FactoryDefaults);

        editor.Add("MoveUp", new InputBinding.Keyboard(Key.Z, false, false, false, false));
        editor.Replace("Attack", 0, new InputBinding.Keyboard(Key.B, false, false, false, false));
        editor.Remove("Attack", 0);
        editor.ResetAction("MoveUp");
        editor.ResetAll();

        Assert.Equal(activeBefore, SnapshotOf(_service.Active));
        Assert.Equal(factoryBefore, SnapshotOf(_service.FactoryDefaults));
    }

    private static void AssertDraft(KeyBindingEditorState editor, params (string Action, InputBinding[] Bindings)[] expectations)
    {
        var rows = editor.Groups.SelectMany(g => g.Rows).ToList();
        foreach (var action in InputActionCatalog.Actions)
        {
            var actual = rows
                .Where(r => r.Action.Name == action.Name && r.Binding is not null)
                .Select(r => r.Binding!)
                .ToArray();
            var expected = expectations.FirstOrDefault(e => e.Action == action.Name).Bindings
                ?? Array.Empty<InputBinding>();
            Assert.Equal(expected, actual);
        }
    }

    private static IReadOnlyList<KeyBindingEditorRow> Rows(KeyBindingEditorState editor, string action) =>
        editor.Groups.SelectMany(g => g.Rows).Where(r => r.Action.Name == action).ToList();

    private static KeyBindingEditorRow Row(KeyBindingEditorState editor, string action, int index) =>
        Assert.Single(Rows(editor, action), r => r.Index == index);

    private static Dictionary<string, InputBinding[]> SnapshotOf(InputBindingSet set) =>
        InputActionCatalog.Actions.ToDictionary(a => a.Name, a => set.GetBindings(a.Name).ToArray());

    private static InputBindingSet BuildFactory()
    {
        var bindings = new Dictionary<string, IReadOnlyList<InputBinding>>();
        foreach (var action in InputActionCatalog.Actions)
            bindings[action.Name] = action.Name == "Attack" ? AttackFactory : Array.Empty<InputBinding>();
        return new InputBindingSet(bindings);
    }

    private sealed class FakeAdapter : IInputMapAdapter
    {
        public InputBindingSet? Factory { get; set; }
        public InputBindingSet? Current { get; private set; }
        public bool FailReplace { get; set; }
        public bool RuntimeRestored { get; set; } = true;

        public InputBindingSet CaptureFactory(IReadOnlyList<InputActionDefinition> catalog) => Factory!;

        public void Replace(InputBindingSet previous, InputBindingSet next)
        {
            if (FailReplace)
            {
                Current = previous;
                throw new InputMapAdapterFailure(
                    RuntimeRestored,
                    new InvalidOperationException("injected adapter failure"),
                    null);
            }

            Current = next;
        }
    }
}
