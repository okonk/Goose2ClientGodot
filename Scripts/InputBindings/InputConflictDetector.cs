using System;
using System.Collections.Generic;
using System.Linq;

namespace Goose2Client.InputBindings;

public sealed class InputBindingConflict
{
    public string FirstAction { get; }
    public string SecondAction { get; }
    public IReadOnlyList<InputBinding> SharedBindings { get; }
    public IReadOnlyList<BindingContext> OverlappingContexts { get; }

    public InputBindingConflict(
        string firstAction,
        string secondAction,
        IReadOnlyList<InputBinding> sharedBindings,
        IReadOnlyList<BindingContext> overlappingContexts)
    {
        FirstAction = firstAction;
        SecondAction = secondAction;
        SharedBindings = sharedBindings;
        OverlappingContexts = overlappingContexts;
    }

    public bool Equals(InputBindingConflict? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return FirstAction == other.FirstAction
            && SecondAction == other.SecondAction
            && SharedBindings.SequenceEqual(other.SharedBindings)
            && OverlappingContexts.SequenceEqual(other.OverlappingContexts);
    }

    public override bool Equals(object? obj) => Equals(obj as InputBindingConflict);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FirstAction);
        hash.Add(SecondAction);
        foreach (var binding in SharedBindings)
            hash.Add(binding);
        foreach (var context in OverlappingContexts)
            hash.Add(context);
        return hash.ToHashCode();
    }

    public static bool operator ==(InputBindingConflict? left, InputBindingConflict? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(InputBindingConflict? left, InputBindingConflict? right) => !(left == right);
}

public static class InputConflictDetector
{
    public static IReadOnlyList<InputBindingConflict> Detect(InputBindingSet snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var conflicts = new List<InputBindingConflict>();
        var actions = InputActionCatalog.Actions;

        for (var i = 0; i < actions.Count; i++)
        {
            var first = actions[i];
            var firstBindings = snapshot.GetBindings(first.Name);

            for (var j = i + 1; j < actions.Count; j++)
            {
                var second = actions[j];
                var shared = SharedBindings(firstBindings, snapshot.GetBindings(second.Name));
                if (shared.Count == 0)
                    continue;

                var contexts = ConflictingContexts(first.Roles, second.Roles);
                if (contexts.Count > 0)
                    conflicts.Add(new InputBindingConflict(first.Name, second.Name, shared.AsReadOnly(), contexts.AsReadOnly()));
            }
        }

        return conflicts.AsReadOnly();
    }

    private static List<InputBinding> SharedBindings(IReadOnlyList<InputBinding> a, IReadOnlyList<InputBinding> b)
    {
        var result = new List<InputBinding>();
        var seen = new HashSet<InputBinding>();

        foreach (var binding in a)
            if (b.Contains(binding) && seen.Add(binding))
                result.Add(binding);

        return result;
    }

    private static List<BindingContext> ConflictingContexts(IReadOnlyList<BindingRole> a, IReadOnlyList<BindingRole> b)
    {
        var contexts = new HashSet<BindingContext>();

        foreach (var roleA in a)
        foreach (var roleB in b)
        {
            if (roleA.Role == roleB.Role)
                continue;

            var context = OverlappingContext(roleA.Context, roleB.Context);
            if (context.HasValue)
                contexts.Add(context.Value);
        }

        return contexts.OrderBy(c => c).ToList();
    }

    private static BindingContext? OverlappingContext(BindingContext a, BindingContext b)
    {
        if (a == b)
            return a;
        if (a == BindingContext.Global)
            return b;
        if (b == BindingContext.Global)
            return a;
        return null;
    }
}
