using System;
using System.Collections.Generic;
using System.Linq;

namespace Goose2Client.InputBindings;

public sealed record InputBindingInitializeResult(bool Success, string? Warning);

public sealed record InputBindingRecoveryStatus(
    bool RuntimeNeedsRecovery,
    bool FileNeedsRecovery,
    Exception OriginalFailure,
    Exception? RuntimeRestorationFailure,
    Exception? FileRollbackFailure);

public sealed record InputBindingApplyResult(
    bool Success,
    string? Error,
    string? Warning,
    InputBindingRecoveryStatus? Recovery);

public sealed class InputBindingService
{
    private readonly IInputMapAdapter _adapter;
    private readonly InputBindingFileStore _store;
    private InputBindingSet? _factoryDefaults;
    private InputBindingSet? _active;

    public InputBindingService(IInputMapAdapter adapter, InputBindingFileStore store)
    {
        _adapter = adapter;
        _store = store;
    }

    public bool IsInitialized => _active is not null;

    public string? StartupWarning { get; private set; }

    public bool CaptureGateHeld { get; private set; }

    public event Action? CaptureGateReleased;

    public InputBindingSet FactoryDefaults =>
        _factoryDefaults ?? throw new InvalidOperationException("Initialize must be called before use.");

    public InputBindingSet Active =>
        _active ?? throw new InvalidOperationException("Initialize must be called before use.");

    public InputBindingInitializeResult Initialize()
    {
        var result = InitializeCore();
        StartupWarning = result.Warning;
        return result;
    }

    private InputBindingInitializeResult InitializeCore()
    {
        if (_active is not null)
            throw new InvalidOperationException("Initialize can only be called once.");

        var factory = _adapter.CaptureFactory(InputActionCatalog.Actions);
        _factoryDefaults = factory;

        var bytes = _store.Read();
        if (bytes is null)
        {
            _active = factory;
            return new InputBindingInitializeResult(true, null);
        }

        var parsed = InputBindingJson.Parse(bytes);
        if (!parsed.Success)
        {
            _active = factory;
            return new InputBindingInitializeResult(
                true, $"Input bindings file could not be loaded and factory defaults are active: {parsed.Error}");
        }

        var resolved = new Dictionary<string, IReadOnlyList<InputBinding>>(InputActionCatalog.Actions.Count);
        foreach (var action in InputActionCatalog.Actions)
            resolved[action.Name] =
                parsed.Overrides!.TryGetValue(action.Name, out var overrideBindings)
                    ? overrideBindings
                    : factory.GetBindings(action.Name);

        var resolvedSet = new InputBindingSet(resolved);
        if (SetEquals(factory, resolvedSet))
        {
            _active = resolvedSet;
            return new InputBindingInitializeResult(true, null);
        }

        try
        {
            _adapter.Replace(factory, resolvedSet);
        }
        catch (InputMapAdapterFailure failure)
        {
            if (!failure.RuntimeRestored)
                throw;

            _active = factory;
            return new InputBindingInitializeResult(
                true,
                "Persisted input bindings could not be applied to the runtime input map; factory defaults are active.");
        }

        _active = resolvedSet;
        return new InputBindingInitializeResult(true, null);
    }

    public InputBindingApplyResult Apply(IReadOnlyDictionary<string, IReadOnlyList<InputBinding>> draft)
    {
        var active = Active;
        var factory = FactoryDefaults;

        var candidate = BuildCandidate(draft, out var validationError);
        if (candidate is null)
            return new InputBindingApplyResult(false, validationError, null, null);

        InputBindingFileStoreTransaction transaction;
        try
        {
            transaction = _store.Save(InputBindingJson.Serialize(candidate, factory));
        }
        catch (Exception ex)
        {
            return new InputBindingApplyResult(false, $"Input bindings file could not be saved: {ex.Message}", null, null);
        }

        try
        {
            _adapter.Replace(active, candidate);
        }
        catch (InputMapAdapterFailure failure)
        {
            var rollbackSucceeded = transaction.Rollback();
            if (failure.RuntimeRestored && rollbackSucceeded)
                return new InputBindingApplyResult(false, failure.OriginalFailure.Message, null, null);

            return new InputBindingApplyResult(false, failure.OriginalFailure.Message, null,
                new InputBindingRecoveryStatus(
                    !failure.RuntimeRestored,
                    !rollbackSucceeded,
                    failure.OriginalFailure,
                    failure.RestorationFailure,
                    rollbackSucceeded ? null : FileRollbackFailure()));
        }
        catch (Exception ex)
        {
            // An untyped Replace failure leaves the runtime map in an unknown state.
            var rollbackSucceeded = transaction.Rollback();
            return new InputBindingApplyResult(false, ex.Message, null,
                new InputBindingRecoveryStatus(
                    true,
                    !rollbackSucceeded,
                    ex,
                    null,
                    rollbackSucceeded ? null : FileRollbackFailure()));
        }

        _active = candidate;

        if (transaction.Complete())
            return new InputBindingApplyResult(true, null, null, null);

        return new InputBindingApplyResult(
            true,
            null,
            "Input bindings were applied but file transaction cleanup failed; a backup artifact may remain.",
            null);
    }

    private static Exception FileRollbackFailure() =>
        new("Input bindings file rollback failed; the file may contain the rejected bindings.");

    private static bool SetEquals(InputBindingSet a, InputBindingSet b)
    {
        foreach (var action in InputActionCatalog.Actions)
            if (!a.GetBindings(action.Name).SequenceEqual(b.GetBindings(action.Name)))
                return false;

        return true;
    }

    private static InputBindingSet? BuildCandidate(IReadOnlyDictionary<string, IReadOnlyList<InputBinding>> draft, out string? error)
    {
        ArgumentNullException.ThrowIfNull(draft);
        error = null;

        var catalog = InputActionCatalog.Actions;
        var resolved = new Dictionary<string, IReadOnlyList<InputBinding>>(catalog.Count);
        foreach (var action in catalog)
        {
            if (!draft.TryGetValue(action.Name, out var bindings))
            {
                error = $"Draft is missing action '{action.Name}'.";
                return null;
            }

            var normalized = InputBindingRules.NormalizeForEditor(bindings);
            if (!normalized.Success)
            {
                error = $"Action '{action.Name}': {normalized.Error}";
                return null;
            }

            resolved[action.Name] = normalized.Bindings;
        }

        if (draft.Count != catalog.Count)
        {
            error = "Draft must contain every catalog action exactly once.";
            return null;
        }

        return new InputBindingSet(resolved);
    }
}
