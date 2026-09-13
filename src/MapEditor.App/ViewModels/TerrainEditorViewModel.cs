using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.App.Terrain;
using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.ViewModels;

internal sealed class TerrainEditorViewModel : ViewModelBase, IDisposable
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 8.0;

    private static readonly IReadOnlyList<TerrainValidationIssue> NoIssues = Array.Empty<TerrainValidationIssue>();
    private static readonly IReadOnlyList<SpriteFrame> NoFrames = Array.Empty<SpriteFrame>();

    private readonly TerrainEditorSession _session;
    private readonly SpriteManifest _manifest;
    private readonly IReadOnlyList<int> _eligibleSheets;

    private List<TerrainEditorItemViewModel> _terrains = new();
    private TerrainEditorItemViewModel? _selected;
    private int? _selectedSheet;
    private IReadOnlyList<SpriteFrame> _eligibleFrames = NoFrames;
    private double _zoom = 1.0;
    private string _pendingName = string.Empty;
    private string _pendingColorText = string.Empty;
    private string? _nameError;
    private string? _colorError;
    private IReadOnlyList<TerrainValidationIssue> _errors = NoIssues;
    private IReadOnlyList<TerrainValidationIssue> _warnings = NoIssues;
    private bool _isDirty;
    private bool _canUndo;
    private bool _canRedo;
    private bool _canSave;

    public event Action? CanvasInvalidated;

    public event Action? SheetInvalidated;

    public TerrainEditorViewModel(TerrainEditorSession session, SpriteManifest manifest)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _eligibleSheets = manifest.SheetIds
            .Where(sheet => manifest.GetFrames(sheet).Any(IsEligibleFrame))
            .ToList();
        _session.Changed += OnSessionChanged;
        _selectedSheet = _eligibleSheets.Count > 0 ? _eligibleSheets[0] : null;
        RefreshEligibleFrames();
        RebuildItems();
        if (_selected is null && _terrains.Count > 0)
        {
            _selected = _terrains[0];
            OnPropertyChanged(nameof(SelectedTerrain));
        }

        SyncPendingText();
        RefreshDiagnostics();
        RefreshCommandState();
    }

    public void Dispose()
        => _session.Changed -= OnSessionChanged;

    public TerrainCatalog CurrentCatalog => _session.CurrentCatalog;

    public IReadOnlyList<TerrainEditorItemViewModel> Terrains => _terrains;

    public TerrainEditorItemViewModel? SelectedTerrain
    {
        get => _selected;
        set
        {
            if (!CommitPending())
            {
                return;
            }

            var target = value is null ? null : _terrains.FirstOrDefault(item => item.Id == value.Id);
            if (ReferenceEquals(target, _selected))
            {
                return;
            }

            _selected = target;
            OnPropertyChanged(nameof(SelectedTerrain));
            SyncPendingText();
        }
    }

    public IReadOnlyList<int> EligibleSheets => _eligibleSheets;

    public int? SelectedSheet
    {
        get => _selectedSheet;
        set
        {
            if (_selectedSheet == value)
            {
                return;
            }

            _selectedSheet = value;
            OnPropertyChanged(nameof(SelectedSheet));
            RefreshEligibleFrames();
        }
    }

    public IReadOnlyList<SpriteFrame> EligibleFrames => _eligibleFrames;

    public double Zoom
    {
        get => _zoom;
        set => SetField(ref _zoom, Math.Clamp(value, MinZoom, MaxZoom));
    }

    public string Name
    {
        get => _pendingName;
        set
        {
            if (SetField(ref _pendingName, value))
            {
                SetField(ref _nameError, null, nameof(NameError));
            }
        }
    }

    public string ColorOverrideText
    {
        get => _pendingColorText;
        set
        {
            if (SetField(ref _pendingColorText, value))
            {
                SetField(ref _colorError, null, nameof(ColorError));
            }
        }
    }

    public string? NameError
    {
        get => _nameError;
        private set => SetField(ref _nameError, value);
    }

    public string? ColorError
    {
        get => _colorError;
        private set => SetField(ref _colorError, value);
    }

    public IReadOnlyList<TerrainValidationIssue> Errors => _errors;

    public IReadOnlyList<TerrainValidationIssue> Warnings => _warnings;

    public bool IsDirty => _isDirty;

    public bool CanUndo => _canUndo;

    public bool CanRedo => _canRedo;

    public bool CanSave => _canSave;

    public void ResetZoom()
        => Zoom = 1.0;

    public Guid? AddTerrain()
    {
        if (!CommitPending())
        {
            return null;
        }

        var id = _session.AddTerrain();
        var item = _terrains.FirstOrDefault(terrain => terrain.Id == id);
        if (!ReferenceEquals(item, _selected))
        {
            _selected = item;
            OnPropertyChanged(nameof(SelectedTerrain));
            SyncPendingText();
        }

        return id;
    }

    public bool DeleteSelectedTerrain()
    {
        if (_selected is null || !CommitPending())
        {
            return false;
        }

        var index = _terrains.IndexOf(_selected);
        var id = _selected.Id;
        _session.DeleteTerrain(id);
        var target = _terrains.Count > 0 ? _terrains[Math.Min(index, _terrains.Count - 1)] : null;
        if (!ReferenceEquals(target, _selected))
        {
            _selected = target;
            OnPropertyChanged(nameof(SelectedTerrain));
            SyncPendingText();
        }

        return true;
    }

    public void ResetColorOverride()
    {
        if (_selected is null)
        {
            return;
        }

        SetField(ref _pendingColorText, string.Empty);
        CommitPending();
    }

    public bool Undo()
    {
        if (!CommitPending())
        {
            return false;
        }

        return _session.Undo();
    }

    public bool Redo()
    {
        if (!CommitPending())
        {
            return false;
        }

        return _session.Redo();
    }

    public void BeginRegionStroke(Guid? value)
        => _session.BeginRegionStroke(value);

    public void VisitRegion(TerrainRegionKey key)
        => _session.VisitRegion(key);

    public bool CompleteRegionStroke()
        => _session.CompleteRegionStroke();

    public void CancelRegionStroke()
        => _session.CancelRegionStroke();

    public void Revert()
    {
        if (!CommitPending())
        {
            return;
        }

        _session.Revert();
    }

    public TerrainEditorPreparedMarkSaved? PrepareMarkSaved()
    {
        if (!CommitPending())
        {
            return null;
        }

        return _session.PrepareMarkSaved(_session.CurrentCatalog);
    }

    public bool CommitPending()
    {
        if (_selected is null)
        {
            NameError = null;
            ColorError = null;
            return true;
        }

        var id = _selected.Id;
        var nameError = ValidateName(id, _pendingName);
        var colorError = TryParseColorOverride(_pendingColorText, out var color)
            ? null
            : "Color must be empty or #RRGGBB.";
        if (nameError is not null || colorError is not null)
        {
            NameError = nameError;
            ColorError = colorError;
            return false;
        }

        NameError = null;
        ColorError = null;
        var current = _session.CurrentCatalog.Terrains.FirstOrDefault(terrain => terrain.Id == id);
        if (current is null)
        {
            return true;
        }

        if (!string.Equals(current.Name, _pendingName, StringComparison.Ordinal))
        {
            _session.RenameTerrain(id, _pendingName);
        }

        if (current.ColorOverride != color)
        {
            _session.SetColorOverride(id, color);
        }

        return true;
    }

    private string? ValidateName(Guid id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name is required.";
        }

        foreach (var terrain in _session.CurrentCatalog.Terrains)
        {
            if (terrain.Id != id && string.Equals(terrain.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return $"A terrain named '{name}' already exists.";
            }
        }

        return null;
    }

    internal static bool TryParseColorOverride(string text, out TerrainColor? color)
    {
        color = null;
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (trimmed.Length != 7 || trimmed[0] != '#')
        {
            return false;
        }

        var value = 0;
        for (var i = 1; i < 7; i++)
        {
            var digit = trimmed[i] switch
            {
                >= '0' and <= '9' => trimmed[i] - '0',
                >= 'a' and <= 'f' => trimmed[i] - 'a' + 10,
                >= 'A' and <= 'F' => trimmed[i] - 'A' + 10,
                _ => -1
            };
            if (digit < 0)
            {
                return false;
            }

            value = value * 16 + digit;
        }

        color = new TerrainColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    private void OnSessionChanged(TerrainEditorChangeKind kind)
    {
        if (kind == TerrainEditorChangeKind.Preview)
        {
            RefreshDiagnostics();
            CanvasInvalidated?.Invoke();
            return;
        }

        RebuildItems();
        RefreshDiagnostics();
        RefreshCommandState();
        CanvasInvalidated?.Invoke();
        SheetInvalidated?.Invoke();
    }

    private void RebuildItems()
    {
        var selectedId = _selected?.Id;
        _terrains.Clear();
        foreach (var terrain in _session.CurrentCatalog.Terrains
            .OrderBy(terrain => terrain.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(terrain => terrain.Id))
        {
            _terrains.Add(new TerrainEditorItemViewModel(terrain));
        }

        OnPropertyChanged(nameof(Terrains));
        var previous = _selected;
        _selected = selectedId is { } id ? _terrains.FirstOrDefault(item => item.Id == id) : null;
        if (!SameItem(previous, _selected))
        {
            OnPropertyChanged(nameof(SelectedTerrain));
        }

        SyncPendingText();
    }

    private static bool SameItem(TerrainEditorItemViewModel? left, TerrainEditorItemViewModel? right)
        => ReferenceEquals(left, right)
            || (left is not null
                && right is not null
                && left.Id == right.Id
                && left.Name == right.Name
                && left.ColorOverrideText == right.ColorOverrideText);

    private void SyncPendingText()
    {
        SetField(ref _pendingName, _selected?.Name ?? string.Empty, nameof(Name));
        SetField(ref _pendingColorText, _selected?.ColorOverrideText ?? string.Empty, nameof(ColorOverrideText));
    }

    private void RefreshDiagnostics()
    {
        var issues = _session.Diagnostics;
        var errors = issues.Where(issue => issue.Severity == TerrainValidationSeverity.Error).ToList();
        var warnings = issues.Where(issue => issue.Severity != TerrainValidationSeverity.Error).ToList();
        if (!_errors.SequenceEqual(errors))
        {
            _errors = errors.AsReadOnly();
            OnPropertyChanged(nameof(Errors));
        }

        if (!_warnings.SequenceEqual(warnings))
        {
            _warnings = warnings.AsReadOnly();
            OnPropertyChanged(nameof(Warnings));
        }
    }

    private void RefreshCommandState()
    {
        SetField(ref _isDirty, _session.IsDirty, nameof(IsDirty));
        SetField(ref _canUndo, _session.CanUndo, nameof(CanUndo));
        SetField(ref _canRedo, _session.CanRedo, nameof(CanRedo));
        SetField(ref _canSave, _session.CanSave, nameof(CanSave));
    }

    private void RefreshEligibleFrames()
    {
        _eligibleFrames = _selectedSheet is { } sheet
            ? _manifest.GetFrames(sheet).Where(IsEligibleFrame).ToList().AsReadOnly()
            : NoFrames;
        OnPropertyChanged(nameof(EligibleFrames));
    }

    private static bool IsEligibleFrame(SpriteFrame frame)
        => frame.SourceRect.Width == SpriteManifest.RequiredTileSize
            && frame.SourceRect.Height == SpriteManifest.RequiredTileSize;
}
