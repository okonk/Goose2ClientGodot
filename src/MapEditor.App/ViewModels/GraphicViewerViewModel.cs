using System;
using System.Collections.Generic;
using MapEditor.Rendering;

namespace MapEditor.App.ViewModels;

internal sealed class GraphicViewerViewModel : ViewModelBase
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 8.0;

    private static readonly IReadOnlyList<int> NoSheets = Array.Empty<int>();
    private static readonly IReadOnlyList<SpriteFrame> NoFrames = Array.Empty<SpriteFrame>();
    private static readonly IReadOnlyList<GraphicCategoryMapping> NoMappings = Array.Empty<GraphicCategoryMapping>();
    private static readonly IReadOnlyList<GraphicAnimation> NoAnimations = Array.Empty<GraphicAnimation>();

    private SpriteManifest? _sprites;
    private GraphicAssetCatalog? _catalog;
    private GraphicViewerCategoryFilter _category = GraphicViewerCategoryFilter.All;
    private IReadOnlyList<int> _sheets = NoSheets;
    private int? _selectedSheetId;
    private IReadOnlyList<SpriteFrame> _frames = NoFrames;
    private SpriteFrame? _selectedFrame;
    private IReadOnlyList<GraphicCategoryMapping> _mappings = NoMappings;
    private IReadOnlyList<GraphicAnimation> _matchingAnimations = NoAnimations;
    private GraphicAnimation? _selectedAnimation;
    private int _frameIndex;
    private bool _isPlaying;
    private double _zoom = 1.0;
    private double _playbackAccumulator;

    public GraphicViewerViewModel(SpriteManifest? sprites = null, GraphicAssetCatalog? catalog = null)
    {
        if (sprites is not null)
        {
            Bind(sprites, catalog);
        }
    }

    public bool IsAvailable => _catalog is not null;

    public GraphicViewerCategoryFilter Category
    {
        get => _category;
        set
        {
            if (_category == value)
            {
                return;
            }

            _category = value;
            OnPropertyChanged(nameof(Category));
            RefreshSheets();
            int? first = _sheets.Count > 0 ? _sheets[0] : null;
            if (first != _selectedSheetId)
            {
                SelectSheet(first);
            }
        }
    }

    public IReadOnlyList<int> Sheets => _sheets;

    public int? SelectedSheetId => _selectedSheetId;

    public IReadOnlyList<SpriteFrame> Frames => _frames;

    public SpriteFrame? SelectedFrame => _selectedFrame;

    public IReadOnlyList<GraphicCategoryMapping> Mappings => _mappings;

    public IReadOnlyList<GraphicAnimation> MatchingAnimations => _matchingAnimations;

    public GraphicAnimation? SelectedAnimation => _selectedAnimation;

    public int FrameIndex => _frameIndex;

    public bool IsPlaying => _isPlaying;

    public SpriteReference? CurrentFrame
        => _selectedAnimation is null ? null : _selectedAnimation.Frames[_frameIndex];

    public double Zoom
    {
        get => _zoom;
        set => SetField(ref _zoom, Math.Clamp(value, MinZoom, MaxZoom));
    }

    public void Bind(SpriteManifest sprites, GraphicAssetCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        _sprites = sprites;
        _catalog = catalog;
        ResetTransientState();
    }

    public void Unbind()
    {
        _sprites = null;
        _catalog = null;
        ResetTransientState();
    }

    public bool TrySelectSheet(int sheetId)
    {
        if (!_sheets.Contains(sheetId))
        {
            return false;
        }

        SelectSheet(sheetId);
        return true;
    }

    public bool TrySelectFrameAt(double x, double y)
    {
        if (_frames.Count == 0)
        {
            return false;
        }

        double sheetX = x / _zoom;
        double sheetY = y / _zoom;
        SpriteFrame? hit = null;
        foreach (SpriteFrame frame in _frames)
        {
            SpriteSourceRect rect = frame.SourceRect;
            if (sheetX >= rect.X && sheetX < rect.X + rect.Width
                && sheetY >= rect.Y && sheetY < rect.Y + rect.Height)
            {
                if (hit is null || frame.Reference.Graphic < hit.Value.Reference.Graphic)
                {
                    hit = frame;
                }
            }
        }

        if (hit is null)
        {
            return false;
        }

        SelectFrame(hit.Value);
        return true;
    }

    public void SelectAnimation(GraphicAnimation? animation)
    {
        _selectedAnimation = animation;
        OnPropertyChanged(nameof(SelectedAnimation));
        _frameIndex = 0;
        OnPropertyChanged(nameof(FrameIndex));
        _playbackAccumulator = 0;
        _isPlaying = animation is not null;
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(CurrentFrame));
    }

    public void Play()
    {
        if (_selectedAnimation is not null)
        {
            SetField(ref _isPlaying, true);
        }
    }

    public void Pause()
        => SetField(ref _isPlaying, false);

    public void StepNext()
        => Step(1);

    public void StepPrevious()
        => Step(-1);

    public void Tick(double elapsedSeconds)
    {
        if (!_isPlaying || _selectedAnimation is null || elapsedSeconds <= 0)
        {
            return;
        }

        _playbackAccumulator += elapsedSeconds;
        double frameDuration = 1.0 / _selectedAnimation.FramesPerSecond;
        int steps = (int)(_playbackAccumulator / frameDuration);
        if (steps <= 0)
        {
            return;
        }

        _playbackAccumulator -= steps * frameDuration;
        int count = _selectedAnimation.Frames.Count;
        int next = (_frameIndex + steps) % count;
        if (SetField(ref _frameIndex, next))
        {
            OnPropertyChanged(nameof(CurrentFrame));
        }
    }

    public void ResetZoom()
        => Zoom = 1.0;

    public void FitToViewport(double viewportWidth, double viewportHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        double imageWidth = 0;
        double imageHeight = 0;
        foreach (SpriteFrame frame in _frames)
        {
            imageWidth = Math.Max(imageWidth, frame.SourceRect.X + frame.SourceRect.Width);
            imageHeight = Math.Max(imageHeight, frame.SourceRect.Y + frame.SourceRect.Height);
        }

        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return;
        }

        Zoom = Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);
    }

    private void ResetTransientState()
    {
        _category = GraphicViewerCategoryFilter.All;
        OnPropertyChanged(nameof(Category));
        _zoom = 1.0;
        OnPropertyChanged(nameof(Zoom));
        _selectedSheetId = null;
        OnPropertyChanged(nameof(SelectedSheetId));
        _frames = NoFrames;
        OnPropertyChanged(nameof(Frames));
        _mappings = NoMappings;
        OnPropertyChanged(nameof(Mappings));
        ClearFrameState();
        RefreshSheets();
    }

    private void RefreshSheets()
    {
        _sheets = _catalog?.GetSheets(ToCategory()) ?? NoSheets;
        OnPropertyChanged(nameof(Sheets));
    }

    private void SelectSheet(int? sheetId)
    {
        if (sheetId == _selectedSheetId)
        {
            return;
        }

        _selectedSheetId = sheetId;
        OnPropertyChanged(nameof(SelectedSheetId));
        _frames = sheetId is null || _sprites is null ? NoFrames : _sprites.GetFrames(sheetId.Value);
        OnPropertyChanged(nameof(Frames));
        _mappings = sheetId is null || _catalog is null ? NoMappings : _catalog.GetMappings(sheetId.Value);
        OnPropertyChanged(nameof(Mappings));
        ClearFrameState();
    }

    private void ClearFrameState()
    {
        _selectedFrame = null;
        OnPropertyChanged(nameof(SelectedFrame));
        _matchingAnimations = NoAnimations;
        OnPropertyChanged(nameof(MatchingAnimations));
        _selectedAnimation = null;
        OnPropertyChanged(nameof(SelectedAnimation));
        _frameIndex = 0;
        OnPropertyChanged(nameof(FrameIndex));
        _isPlaying = false;
        OnPropertyChanged(nameof(IsPlaying));
        _playbackAccumulator = 0;
        OnPropertyChanged(nameof(CurrentFrame));
    }

    private void SelectFrame(SpriteFrame frame)
    {
        _selectedFrame = frame;
        OnPropertyChanged(nameof(SelectedFrame));
        _matchingAnimations = _catalog?.GetAnimations(frame.Reference) ?? NoAnimations;
        OnPropertyChanged(nameof(MatchingAnimations));
        _frameIndex = 0;
        OnPropertyChanged(nameof(FrameIndex));
        _playbackAccumulator = 0;
        if (_matchingAnimations.Count > 0)
        {
            _selectedAnimation = _matchingAnimations[0];
            _isPlaying = true;
        }
        else
        {
            _selectedAnimation = null;
            _isPlaying = false;
        }

        OnPropertyChanged(nameof(SelectedAnimation));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(CurrentFrame));
    }

    private void Step(int direction)
    {
        if (_selectedAnimation is null)
        {
            return;
        }

        int count = _selectedAnimation.Frames.Count;
        int next = (_frameIndex + direction + count) % count;
        if (SetField(ref _frameIndex, next))
        {
            OnPropertyChanged(nameof(CurrentFrame));
        }
    }

    private GraphicCategory? ToCategory()
        => _category == GraphicViewerCategoryFilter.All
            ? null
            : (GraphicCategory)(Convert.ToInt32(_category) - 1);
}
