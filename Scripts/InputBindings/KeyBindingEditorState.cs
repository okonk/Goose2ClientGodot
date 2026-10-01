using System;
using System.Collections.Generic;
using System.Linq;

namespace Goose2Client.InputBindings;

public sealed record KeyBindingEditorRow(
    InputActionDefinition Action,
    int Index,
    InputBinding? Binding,
    bool IsDirty,
    IReadOnlyList<InputBindingConflict> Conflicts);

public sealed record KeyBindingEditorGroup(string Category, IReadOnlyList<KeyBindingEditorRow> Rows);

public sealed class KeyBindingEditorState
{
    private readonly InputBindingService _service;
    private InputBindingSet _baseline = null!;
    private Dictionary<string, List<InputBinding>> _draft = null!;
    private string? _startupWarning;
    private string? _error;
    private string _search = string.Empty;
    private IReadOnlyList<KeyBindingEditorGroup> _groups = [];
    private string _statusText = string.Empty;

    public KeyBindingEditorState(InputBindingService service, string? startupWarning = null)
    {
        _service = service;
        _startupWarning = startupWarning;
        Open();
    }

    public bool IsDirty { get; private set; }

    public string? StartupWarning => _startupWarning;

    public string? Error => _error;

    public string Search
    {
        get => _search;
        set
        {
            _search = value ?? string.Empty;
            Recompute();
        }
    }

    public IReadOnlyList<KeyBindingEditorGroup> Groups => _groups;

    public string StatusText => _statusText;

    public void Open()
    {
        _baseline = _service.Active.Clone();
        _draft = CloneMutable(_service.Active);
        _error = null;
        Recompute();
    }

    public bool Add(string action, InputBinding binding)
    {
        var list = DraftList(action);
        var original = new List<InputBinding>(list);
        list.Add(binding);
        return CommitIfValid(list, original);
    }

    public bool Replace(string action, int index, InputBinding binding)
    {
        var list = DraftList(action);
        if (index < 0 || index >= list.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        var original = new List<InputBinding>(list);
        list[index] = binding;
        return CommitIfValid(list, original);
    }

    public bool Remove(string action, int index)
    {
        var list = DraftList(action);
        if (index < 0 || index >= list.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        list.RemoveAt(index);
        Recompute();
        return true;
    }

    public void ResetAction(string action)
    {
        var list = DraftList(action);
        list.Clear();
        list.AddRange(_service.FactoryDefaults.GetBindings(action));
        Recompute();
    }

    public bool IsFactoryDefault(string action) =>
        DraftList(action).SequenceEqual(_service.FactoryDefaults.GetBindings(action));

    public bool IsFactoryBinding(string action, InputBinding binding) =>
        _service.FactoryDefaults.GetBindings(action).Contains(binding);

    public void ResetAll()
    {
        _draft = CloneMutable(_service.FactoryDefaults);
        Recompute();
    }

    public InputBindingApplyResult Apply()
    {
        var result = _service.Apply(DraftView());
        if (result.Success)
        {
            _baseline = _service.Active.Clone();
            _draft = CloneMutable(_service.Active);
            _startupWarning = null;
        }
        _error = result.Success ? null : result.Error;
        Recompute();
        return result;
    }

    public void Cancel()
    {
        _baseline = _service.Active.Clone();
        _draft = CloneMutable(_service.Active);
        _error = null;
        Recompute();
    }

    private List<InputBinding> DraftList(string action)
    {
        if (!_draft.TryGetValue(action, out var list))
            throw new ArgumentException($"Unknown action: {action}.", nameof(action));
        return list;
    }

    private bool CommitIfValid(List<InputBinding> list, List<InputBinding> original)
    {
        var normalized = InputBindingRules.NormalizeForEditor(list);
        if (!normalized.Success)
        {
            list.Clear();
            list.AddRange(original);
            return false;
        }

        list.Clear();
        list.AddRange(normalized.Bindings);
        Recompute();
        return true;
    }

    private void Recompute()
    {
        IsDirty = !DraftEqualsBaseline();

        var conflicts = InputConflictDetector.Detect(new InputBindingSet(DraftView()));
        var groupRows = new List<(string Category, List<KeyBindingEditorRow> Rows)>();

        foreach (var action in InputActionCatalog.Actions)
        {
            if (!MatchesSearch(action))
                continue;

            if (groupRows.Count == 0 || groupRows[^1].Category != action.Category)
                groupRows.Add((action.Category, new List<KeyBindingEditorRow>()));

            var draft = _draft[action.Name];
            var dirty = !draft.SequenceEqual(_baseline.GetBindings(action.Name));
            if (draft.Count == 0)
            {
                groupRows[^1].Rows.Add(new KeyBindingEditorRow(action, 0, null, dirty, []));
                continue;
            }

            for (var i = 0; i < draft.Count; i++)
            {
                var binding = draft[i];
                var rowConflicts = conflicts
                    .Where(c => (c.FirstAction == action.Name || c.SecondAction == action.Name)
                        && c.SharedBindings.Contains(binding))
                    .ToList();
                groupRows[^1].Rows.Add(new KeyBindingEditorRow(action, i, binding, dirty, rowConflicts));
            }
        }

        _groups = groupRows
            .Select(g => new KeyBindingEditorGroup(g.Category, g.Rows.AsReadOnly()))
            .ToList();

        _statusText = _error
            ?? _startupWarning
            ?? (_search.Length > 0 && _groups.Count == 0 ? $"No actions match \"{_search}\"." : null)
            ?? (IsDirty ? "Unsaved changes." : "No unsaved changes.");
    }

    private bool MatchesSearch(InputActionDefinition action)
    {
        if (_search.Length == 0)
            return true;
        return action.Label.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || action.Category.Contains(_search, StringComparison.OrdinalIgnoreCase);
    }

    private bool DraftEqualsBaseline()
    {
        foreach (var action in InputActionCatalog.Actions)
            if (!_draft[action.Name].SequenceEqual(_baseline.GetBindings(action.Name)))
                return false;
        return true;
    }

    private static Dictionary<string, List<InputBinding>> CloneMutable(InputBindingSet set)
    {
        var result = new Dictionary<string, List<InputBinding>>(InputActionCatalog.Actions.Count);
        foreach (var action in InputActionCatalog.Actions)
            result[action.Name] = new List<InputBinding>(set.GetBindings(action.Name));
        return result;
    }

    private Dictionary<string, IReadOnlyList<InputBinding>> DraftView()
    {
        var view = new Dictionary<string, IReadOnlyList<InputBinding>>(_draft.Count);
        foreach (var pair in _draft)
            view[pair.Key] = pair.Value;
        return view;
    }
}
