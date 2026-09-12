using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using MapEditor.App.Controls;
using MapEditor.App.Rendering;
using MapEditor.App.ViewModels;
using MapEditor.Rendering;

namespace MapEditor.App;

internal partial class GraphicViewerWindow : Window
{
    private static readonly double[] ZoomOptions = { 0.25, 0.5, 1.0, 2.0, 4.0, 8.0 };
    private static readonly IBrush FieldErrorBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x55, 0x55));

    private readonly AssetContextController _assets;
    private readonly ISpriteSheetLoader _loader;
    private readonly IPlaybackClock _clock;
    private readonly GraphicViewerViewModel _viewModel;
    private readonly GraphicSheetControl _sheetControl;
    private readonly AnimationPreviewControl _previewControl;
    private AvaloniaSpriteSheetImage? _sheetImage;

    public GraphicViewerWindow(AssetContextController assets)
        : this(assets, new AvaloniaSpriteSheetLoader(), new DispatcherPlaybackClock())
    {
    }

    internal GraphicViewerWindow(AssetContextController assets, ISpriteSheetLoader loader, IPlaybackClock clock)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _viewModel = new GraphicViewerViewModel();
        InitializeComponent();
        _sheetControl = new GraphicSheetControl(_viewModel);
        _previewControl = new AnimationPreviewControl(_viewModel, _assets.Current);
        SheetHost.Child = _sheetControl;
        PreviewHost.Child = _previewControl;

        CategoryCombo.ItemsSource = Enum.GetValues<GraphicViewerCategoryFilter>();
        CategoryCombo.SelectionChanged += OnCategorySelectionChanged;
        ZoomCombo.ItemsSource = ZoomOptions;
        ZoomCombo.SelectionChanged += OnZoomSelectionChanged;
        AnimationCombo.SelectionChanged += OnAnimationSelectionChanged;
        SheetField.KeyDown += OnSheetFieldKeyDown;
        SheetField.LostFocus += OnSheetFieldLostFocus;
        _previewControl.DiagnosticChanged += OnPreviewDiagnosticChanged;
        _clock.Tick += OnClockTick;
        _assets.CurrentChanged += OnAssetsChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
        ApplyContext(_assets.Current);
    }

    internal GraphicViewerViewModel ViewModel => _viewModel;

    internal GraphicSheetControl SheetControl => _sheetControl;

    internal AnimationPreviewControl PreviewControl => _previewControl;

    internal AvaloniaSpriteSheetImage? SheetImage => _sheetImage;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None || e.Source is TextBox)
        {
            return;
        }

        TogglePlayback();
        e.Handled = true;
    }

    private void OnAssetsChanged(object? sender, EventArgs e)
        => ApplyContext(_assets.Current);

    private void ApplyContext(AssetContext context)
    {
        _clock.Stop();
        _viewModel.Unbind();
        _previewControl.Assets = context;
        if (!context.GraphicViewerAvailability.IsAvailable)
        {
            SyncAvailability(context);
            return;
        }

        _viewModel.Bind(context.Cache.Manifest!, context.Graphics);
        if (_viewModel.Sheets.Count > 0)
        {
            _viewModel.TrySelectSheet(_viewModel.Sheets[0]);
        }

        SyncAvailability(context);
    }

    private void SyncAvailability(AssetContext context)
    {
        GraphicViewerAvailability availability = context.GraphicViewerAvailability;
        AvailabilityText.Text = availability.IsAvailable ? "available" : availability.Diagnostic!;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GraphicViewerViewModel.SelectedSheetId):
                SyncSheetField();
                LoadSheetImage(_viewModel.SelectedSheetId);
                break;
            case nameof(GraphicViewerViewModel.IsPlaying):
                SyncClock();
                SyncPlaybackChrome();
                break;
            case nameof(GraphicViewerViewModel.SelectedAnimation):
                SyncClock();
                AnimationCombo.SelectedItem = _viewModel.SelectedAnimation;
                SyncPlaybackChrome();
                break;
            case nameof(GraphicViewerViewModel.FrameIndex):
                SyncPlaybackChrome();
                break;
            case nameof(GraphicViewerViewModel.Category):
                CategoryCombo.SelectedItem = _viewModel.Category;
                break;
            case nameof(GraphicViewerViewModel.Zoom):
                SyncZoomCombo();
                break;
            case nameof(GraphicViewerViewModel.MatchingAnimations):
                AnimationCombo.ItemsSource = _viewModel.MatchingAnimations;
                break;
            case nameof(GraphicViewerViewModel.Mappings):
                MappingsList.ItemsSource = _viewModel.Mappings;
                break;
            case nameof(GraphicViewerViewModel.SelectedFrame):
                SyncSelectionReadout();
                break;
        }
    }

    // A sheet change must stop the clock before the view model's own playback reset lands, so a tick
    // cannot advance state while the old sheet image is being replaced.
    private void LoadSheetImage(int? sheetId)
    {
        _clock.Stop();
        _sheetControl.Image = null;
        _sheetImage?.Dispose();
        _sheetImage = null;
        LoadDiagnosticText.Text = "—";

        if (sheetId is not { } id || !_assets.Current.IsAvailable)
        {
            return;
        }

        string path = Path.Combine(_assets.Current.Cache.AssetDirectory, "sheets", $"{id}.png");
        SpriteSheetLoadResult result = _loader.Load(path);
        if (result.Status is not SpriteSheetLoadStatus.Success
            || result.Image is not AvaloniaSpriteSheetImage image
            || !string.IsNullOrEmpty(result.Diagnostic))
        {
            result.Image?.Dispose();
            LoadDiagnosticText.Text = result.Diagnostic ?? $"Loader returned a malformed success result for {path}.";
            return;
        }

        _sheetImage = image;
        _sheetControl.Image = image;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void SyncClock()
    {
        _clock.Stop();
        if (_viewModel.SelectedAnimation is { } animation)
        {
            _clock.Interval = TimeSpan.FromMilliseconds(1000.0 / animation.FramesPerSecond);
        }

        if (_viewModel.IsPlaying)
        {
            _clock.Start();
        }
    }

    private void SyncPlaybackChrome()
    {
        bool hasAnimation = _viewModel.SelectedAnimation is not null;
        PlayPauseButton.IsEnabled = hasAnimation;
        PreviousButton.IsEnabled = hasAnimation;
        NextButton.IsEnabled = hasAnimation;
        PlayPauseButton.Content = _viewModel.IsPlaying ? "Pause" : "Play";
        int frameCount = _viewModel.SelectedAnimation?.Frames.Count ?? 0;
        FramePositionText.Text = frameCount == 0 ? "—" : $"{_viewModel.FrameIndex + 1}/{frameCount}";
    }

    private void SyncSelectionReadout()
    {
        if (_viewModel.SelectedFrame is not { } frame)
        {
            GraphicIdText.Text = "—";
            SourceRectText.Text = "—";
            return;
        }

        SpriteSourceRect rect = frame.SourceRect;
        GraphicIdText.Text = frame.Reference.Graphic.ToString();
        SourceRectText.Text = $"{rect.X}, {rect.Y}  {rect.Width} × {rect.Height}";
    }

    private void SyncSheetField()
    {
        string text = _viewModel.SelectedSheetId?.ToString() ?? string.Empty;
        if (SheetField.Text != text)
        {
            SheetField.Text = text;
        }
    }

    private void SyncZoomCombo()
    {
        double zoom = _viewModel.Zoom;
        ZoomCombo.SelectedItem = ZoomOptions.Contains(zoom) ? zoom : (object?)null;
    }

    private void OnCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CategoryCombo.SelectedItem is GraphicViewerCategoryFilter category)
        {
            _viewModel.Category = category;
        }
    }

    private void OnZoomSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ZoomCombo.SelectedItem is double zoom)
        {
            _viewModel.Zoom = zoom;
        }
    }

    private void OnAnimationSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => _viewModel.SelectAnimation(AnimationCombo.SelectedItem as GraphicAnimation);

    private void OnSheetFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitSheetField();
            e.Handled = true;
        }
    }

    private void OnSheetFieldLostFocus(object? sender, RoutedEventArgs e)
        => CommitSheetField();

    private void CommitSheetField()
    {
        if (int.TryParse(SheetField.Text, out int sheetId) && _viewModel.TrySelectSheet(sheetId))
        {
            SheetField.BorderBrush = null;
            SheetFieldError.IsVisible = false;
            return;
        }

        SheetField.BorderBrush = FieldErrorBrush;
        SheetFieldError.IsVisible = true;
    }

    private void OnFit(object? sender, RoutedEventArgs e)
    {
        if (_sheetImage is not { } image)
        {
            return;
        }

        double viewportWidth = SheetScroll.Viewport.Width - SheetHost.Padding.Left - SheetHost.Padding.Right;
        double viewportHeight = SheetScroll.Viewport.Height - SheetHost.Padding.Top - SheetHost.Padding.Bottom;
        _viewModel.FitToViewport(viewportWidth, viewportHeight, image.PixelWidth, image.PixelHeight);
    }

    private void OnPercent100(object? sender, RoutedEventArgs e)
        => _viewModel.ResetZoom();

    private void OnPlayPause(object? sender, RoutedEventArgs e)
        => TogglePlayback();

    private void OnPrevious(object? sender, RoutedEventArgs e)
        => _viewModel.StepPrevious();

    private void OnNext(object? sender, RoutedEventArgs e)
        => _viewModel.StepNext();

    private void TogglePlayback()
    {
        if (_viewModel.IsPlaying)
        {
            _viewModel.Pause();
        }
        else
        {
            _viewModel.Play();
        }
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        _viewModel.Tick(_clock.Interval.TotalSeconds);
        _previewControl.InvalidateVisual();
    }

    private void OnPreviewDiagnosticChanged(object? sender, EventArgs e)
    {
        PreviewDiagnosticText.Text = _previewControl.Diagnostic ?? string.Empty;
        PreviewDiagnosticText.IsVisible = _previewControl.Diagnostic is not null;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _assets.CurrentChanged -= OnAssetsChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _clock.Tick -= OnClockTick;
        SheetHost.Child = null;
        PreviewHost.Child = null;
        _sheetControl.Image = null;
        _sheetImage?.Dispose();
        _sheetImage = null;
        _clock.Dispose();
    }
}
