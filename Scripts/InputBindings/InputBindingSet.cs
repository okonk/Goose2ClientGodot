using System;
using System.Collections.Generic;
using System.Linq;

namespace Goose2Client.InputBindings;

public sealed class InputBindingSet
{
    private readonly Dictionary<string, IReadOnlyList<InputBinding>> _bindings;

    public InputBindingSet(IReadOnlyDictionary<string, IReadOnlyList<InputBinding>> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var catalog = InputActionCatalog.Actions.Select(a => a.Name).ToHashSet();
        if (bindings.Count != catalog.Count)
            throw new ArgumentException("Binding set must contain every catalog action exactly once.", nameof(bindings));

        _bindings = new Dictionary<string, IReadOnlyList<InputBinding>>(bindings.Count);
        foreach (var pair in bindings)
        {
            if (!catalog.Contains(pair.Key))
                throw new ArgumentException($"Unknown action: {pair.Key}.", nameof(bindings));

            ArgumentNullException.ThrowIfNull(pair.Value);
            _bindings[pair.Key] = new List<InputBinding>(pair.Value).AsReadOnly();
        }

        if (_bindings.Count != catalog.Count)
            throw new ArgumentException("Binding set must contain every catalog action exactly once.", nameof(bindings));
    }

    public IReadOnlyList<InputBinding> GetBindings(string action)
    {
        if (!_bindings.TryGetValue(action, out var bindings))
            throw new KeyNotFoundException(action);

        return bindings;
    }

    public bool TryGetBindings(string action, out IReadOnlyList<InputBinding> bindings)
    {
        return _bindings.TryGetValue(action, out bindings!);
    }

    public InputBindingSet Clone() => new(_bindings);
}
