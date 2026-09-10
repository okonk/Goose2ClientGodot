using Avalonia.Controls;
using Avalonia.Interactivity;
using MapEditor.App.Terrain;
using MapEditor.Core.Terrain;

namespace MapEditor.App.Dialogs;

internal partial class TerrainSetsDialog : Window
{
    private readonly TerrainCatalogManager _manager;
    private readonly Func<Task<DirtyChoice>> _confirmDirtyAsync;
    private bool _refreshing;
    private bool _closeApproved;
    private bool _closePending;

    internal TerrainSetsDialog(TerrainCatalogManager manager, Func<Task<DirtyChoice>> confirmDirtyAsync)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _confirmDirtyAsync = confirmDirtyAsync ?? throw new ArgumentNullException(nameof(confirmDirtyAsync));
        InitializeComponent();
        TerrainMaskPreview.Configure(manager.SourceContext);
        TerrainMaskPreview.DiagnosticChanged += diagnostic => PreviewDiagnosticText.Text = diagnostic;
        TerrainStatusCombo.ItemsSource = Enum.GetValues<TerrainReviewStatus>();
        TerrainTopologyCombo.ItemsSource = new[] { TerrainTopology.FourWay, TerrainTopology.EightWay };
        TerrainSetList.SelectionChanged += OnSetSelectionChanged;
        TerrainMaskList.SelectionChanged += OnMaskSelectionChanged;
        TerrainVariantList.SelectionChanged += (_, _) => RefreshButtons();
        TerrainNameBox.LostFocus += OnNameCommitted;
        TerrainStatusCombo.SelectionChanged += OnStatusChanged;
        TerrainTopologyCombo.SelectionChanged += OnTopologyChanged;
        RegenerateIdButton.Click += (_, _) => Mutate(() => _manager.ViewModel.RegenerateId(SelectedSet!.Key));
        AddVariantButton.Click += OnAddVariant;
        RemoveVariantButton.Click += (_, _) => MutateSelectedVariant(remove: true, 0);
        MoveVariantUpButton.Click += (_, _) => MutateSelectedVariant(remove: false, -1);
        MoveVariantDownButton.Click += (_, _) => MutateSelectedVariant(remove: false, 1);
        RemoveOrphanMaskButton.Click += OnRemoveOrphan;
        RemoveAllOrphansButton.Click += (_, _) => Mutate(() => _manager.ViewModel.RemoveAllOrphanMasks(SelectedSet!.Key));
        SaveButton.Click += OnSave;
        CancelButton.Click += (_, _) => Close();
        Closing += OnClosing;
        TerrainSetList.ItemsSource = _manager.ViewModel.Sets;
        TerrainSetList.SelectedIndex = _manager.ViewModel.Sets.Count == 0 ? -1 : 0;
        Refresh();
    }

    private TerrainSetDraft? SelectedSet => TerrainSetList.SelectedItem as TerrainSetDraft;
    private TerrainMaskDraft? SelectedMask => TerrainMaskList.SelectedItem as TerrainMaskDraft;

    private void OnSetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing)
            _manager.ViewModel.SelectedSetKey = SelectedSet?.Key;
        Refresh();
    }

    private void OnMaskSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshVariants();
        RefreshButtons();
    }

    private void OnNameCommitted(object? sender, RoutedEventArgs e)
    {
        if (!_refreshing && SelectedSet is { } set)
        {
            _manager.ViewModel.Rename(set.Key, TerrainNameBox.Text ?? string.Empty);
            Refresh(set.Key);
        }
    }

    private void OnStatusChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || SelectedSet is not { } set || TerrainStatusCombo.SelectedItem is not TerrainReviewStatus status)
            return;
        if (!_manager.ViewModel.TrySetStatus(set.Key, status, out IReadOnlyList<TerrainValidationIssue> issues))
        {
            ShowIssues(issues);
            Refresh(set.Key);
            ValidationSummary.Focus();
            return;
        }
        Refresh(set.Key);
    }

    private void OnTopologyChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && SelectedSet is { } set && TerrainTopologyCombo.SelectedItem is TerrainTopology topology)
            Mutate(() => _manager.ViewModel.ChangeTopology(set.Key, topology));
    }

    private void OnAddVariant(object? sender, RoutedEventArgs e)
    {
        if (SelectedSet is not { } set || SelectedMask is not { } mask ||
            !int.TryParse(AddVariantSheetBox.Text, out int sheet) ||
            !int.TryParse(AddVariantGraphicBox.Text, out int graphic))
        {
            ValidationSummary.Text = "Sheet and graphic must be signed integers, and a mask must be selected.";
            ValidationSummary.Focus();
            return;
        }
        Mutate(() => _manager.ViewModel.AddVariant(set.Key, mask.Mask, new TerrainGraphicReference(sheet, graphic)));
    }

    private void MutateSelectedVariant(bool remove, int offset)
    {
        if (SelectedSet is not { } set || SelectedMask is not { } mask || TerrainVariantList.SelectedIndex is not >= 0)
            return;
        int index = TerrainVariantList.SelectedIndex;
        if (remove)
            Mutate(() => _manager.ViewModel.RemoveVariant(set.Key, mask.Mask, index));
        else
        {
            int target = index + offset;
            if (target >= 0 && target < mask.Variants.Count)
                Mutate(() => _manager.ViewModel.ReorderVariant(set.Key, mask.Mask, index, target), target);
        }
    }

    private void OnRemoveOrphan(object? sender, RoutedEventArgs e)
    {
        if (SelectedSet is { } set && SelectedMask is { } mask && set.OrphanMasks.Any(item => ReferenceEquals(item, mask)))
            Mutate(() => _manager.ViewModel.RemoveOrphanMask(set.Key, mask.Mask));
    }

    private void Mutate(Func<TerrainDraftMutationResult> mutation, int selectedVariant = -1)
    {
        TerrainDraftKey? key = SelectedSet?.Key;
        TerrainDraftMutationResult result = mutation();
        if (!result.Succeeded)
            ShowIssues(result.Issues);
        Refresh(key);
        if (selectedVariant >= 0)
            TerrainVariantList.SelectedIndex = selectedVariant;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        TerrainCatalogSaveResult result = _manager.Save();
        Present(result);
        if (result.ClosesDialog)
        {
            _closeApproved = true;
            Close(result);
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved || !_manager.ViewModel.IsDirty)
            return;
        e.Cancel = true;
        if (_closePending)
            return;
        _closePending = true;
        try
        {
            DirtyChoice choice = await _confirmDirtyAsync();
            if (choice == DirtyChoice.Save)
            {
                TerrainCatalogSaveResult result = _manager.Save();
                Present(result);
                if (!result.ClosesDialog)
                    return;
                _closeApproved = true;
                Close(result);
            }
            else if (choice == DirtyChoice.Discard)
            {
                _closeApproved = true;
                Close();
            }
        }
        finally
        {
            _closePending = false;
        }
    }

    private void Present(TerrainCatalogSaveResult result)
    {
        if (result.Status == TerrainCatalogSaveStatus.InvalidDraft)
            ShowIssues(result.Issues);
        else
            ValidationSummary.Text = result.Status switch
            {
                TerrainCatalogSaveStatus.Succeeded => "Terrain sets saved and published.",
                TerrainCatalogSaveStatus.NoChanges => "No terrain set changes to save.",
                TerrainCatalogSaveStatus.SourceContextChanged => result.Failure!.Message,
                TerrainCatalogSaveStatus.ReplacementInProgress => result.Failure!.Message,
                TerrainCatalogSaveStatus.GestureCancellationFailed => $"Terrain gesture cancellation failed: {result.Failure!.Message}",
                TerrainCatalogSaveStatus.WriteFailed => $"Terrain sets could not be saved: {result.Failure!.Message}",
                _ => result.Status.ToString()
            };
        if (!result.ClosesDialog)
            ValidationSummary.Focus();
    }

    private void Refresh(TerrainDraftKey? selectedKey = null)
    {
        _refreshing = true;
        try
        {
            selectedKey ??= SelectedSet?.Key ?? _manager.ViewModel.SelectedSetKey;
            TerrainSetList.ItemsSource = _manager.ViewModel.Sets;
            TerrainSetList.SelectedItem = selectedKey is { } key
                ? _manager.ViewModel.Sets.FirstOrDefault(set => set.Key == key)
                : null;
            TerrainSetDraft? set = SelectedSet;
            _manager.ViewModel.SelectedSetKey = set?.Key;
            TerrainNameBox.Text = set?.DisplayName ?? string.Empty;
            TerrainStatusCombo.SelectedItem = set?.Status;
            TerrainTopologyCombo.SelectedItem = set?.Topology;
            TerrainMemberList.ItemsSource = set?.Members;
            TerrainDiagnosticList.ItemsSource = set is null
                ? Array.Empty<object>()
                : set.Diagnostics.Cast<object>().Concat(set.Issues.Cast<object>()).ToArray();
            TerrainMaskList.ItemsSource = set is null
                ? Array.Empty<TerrainMaskDraft>()
                : set.Masks.Concat(set.OrphanMasks).ToArray();
            TerrainMaskList.SelectedIndex = set is { Masks.Count: > 0 } ? 0 : -1;
            ShowIssues(_manager.ViewModel.Issues);
            Title = _manager.ViewModel.IsDirty ? "Terrain Sets *" : "Terrain Sets";
        }
        finally
        {
            _refreshing = false;
        }
        RefreshVariants();
        RefreshButtons();
    }

    private void RefreshVariants()
    {
        TerrainMaskDraft? mask = SelectedMask;
        TerrainVariantList.ItemsSource = mask?.Variants;
        TerrainMaskPreview.SetVariants(mask?.Variants.Select(variant => variant.Reference) ?? Array.Empty<TerrainGraphicReference>());
        PreviewDiagnosticText.Text = TerrainMaskPreview.Diagnostic;
    }

    private void RefreshButtons()
    {
        bool hasSet = SelectedSet is not null;
        bool hasMask = SelectedMask is not null;
        int index = TerrainVariantList.SelectedIndex;
        RegenerateIdButton.IsEnabled = hasSet;
        AddVariantButton.IsEnabled = hasSet && hasMask;
        RemoveVariantButton.IsEnabled = index >= 0;
        MoveVariantUpButton.IsEnabled = index > 0;
        MoveVariantDownButton.IsEnabled = SelectedMask is { } mask && index >= 0 && index + 1 < mask.Variants.Count;
        RemoveOrphanMaskButton.IsEnabled = SelectedSet is { } set && SelectedMask is { } selected && set.OrphanMasks.Any(mask => ReferenceEquals(mask, selected));
        RemoveAllOrphansButton.IsEnabled = SelectedSet is { OrphanMasks.Count: > 0 };
        SaveButton.IsEnabled = _manager.ViewModel.IsDirty;
    }

    private void ShowIssues(IEnumerable<TerrainValidationIssue> issues)
    {
        TerrainValidationIssue[] values = issues.ToArray();
        ValidationSummary.Text = values.Length == 0
            ? string.Empty
            : string.Join(Environment.NewLine, values.Select(issue => $"{issue.Code}: {issue.Message}"));
    }
}
