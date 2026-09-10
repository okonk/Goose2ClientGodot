using MapEditor.App.Rendering;
using MapEditor.App.Terrain;
using MapEditor.Core.Terrain;

namespace MapEditor.App.ViewModels;

internal sealed class TerrainSetsManagerViewModel : ViewModelBase
{
    private readonly TerrainCatalogDraft _draft;
    private readonly int _creatingThreadId;
    private TerrainDraftKey? _selectedSetKey;

    internal TerrainSetsManagerViewModel(TerrainCatalogDraft draft)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _creatingThreadId = Environment.CurrentManagedThreadId;
        _draft.Changed += OnDraftChanged;
    }

    public TerrainDraftKey? SelectedSetKey
    {
        get => _selectedSetKey;
        set
        {
            if (Environment.CurrentManagedThreadId != _creatingThreadId)
                throw new InvalidOperationException("Terrain manager operations must run on the creating thread.");
            if (value is { } key && !_draft.Sets.Any(set => set.Key == key))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (SetField(ref _selectedSetKey, value))
                OnPropertyChanged(nameof(SelectedSet));
        }
    }

    internal IReadOnlyList<TerrainSetDraft> Sets => _draft.Sets;
    internal TerrainSetDraft? SelectedSet => SelectedSetKey is { } key ? Sets.FirstOrDefault(set => set.Key == key) : null;
    internal IReadOnlyList<TerrainValidationIssue> Issues => _draft.Issues;
    internal bool IsDirty => _draft.IsDirty;
    internal bool CanSave => _draft.CanSave;

    internal void Rename(TerrainDraftKey key, string displayName) => _draft.Rename(key, displayName);
    internal TerrainDraftMutationResult RegenerateId(TerrainDraftKey key) => _draft.RegenerateId(key);
    internal TerrainDraftMutationResult ChangeTopology(TerrainDraftKey key, TerrainTopology topology) => _draft.ChangeTopology(key, topology);
    internal TerrainDraftMutationResult AddVariant(TerrainDraftKey key, int mask, TerrainGraphicReference reference) => _draft.AddVariant(key, mask, reference);
    internal TerrainDraftMutationResult RemoveVariant(TerrainDraftKey key, int mask, int index) => _draft.RemoveVariant(key, mask, index);
    internal TerrainDraftMutationResult ReorderVariant(TerrainDraftKey key, int mask, int fromIndex, int toIndex) => _draft.ReorderVariant(key, mask, fromIndex, toIndex);
    internal TerrainDraftMutationResult RemoveOrphanMask(TerrainDraftKey key, int mask) => _draft.RemoveOrphanMask(key, mask);
    internal TerrainDraftMutationResult RemoveAllOrphanMasks(TerrainDraftKey key) => _draft.RemoveAllOrphanMasks(key);
    internal bool TrySetStatus(TerrainDraftKey key, TerrainReviewStatus status, out IReadOnlyList<TerrainValidationIssue> issues) => _draft.TrySetStatus(key, status, out issues);
    internal TerrainCatalog Build() => _draft.Build();
    internal IReadOnlyDictionary<string, string> BuildPublishedIdRekeys() => _draft.BuildPublishedIdRekeys();
    internal void AcceptChanges() => _draft.AcceptChanges();

    internal TerrainManagerPublicationPlan PlanAcceptSavedCatalog()
        => new(this, _draft.PlanAcceptChanges(), new[]
        {
            nameof(Sets),
            nameof(SelectedSet),
            nameof(Issues),
            nameof(IsDirty),
            nameof(CanSave)
        });

    internal void CommitSavedCatalog(TerrainManagerPublicationPlan plan)
        => _draft.CommitAcceptChanges(plan.Acceptance);

    internal void NotifySavedCatalog(TerrainManagerPublicationPlan plan, Action<string, Exception> failure)
    {
        foreach (string property in plan.Properties)
            OnPropertyChangedSafely(property, failure);
    }

    private void OnDraftChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Sets));
        OnPropertyChanged(nameof(SelectedSet));
        OnPropertyChanged(nameof(Issues));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
    }
}
