using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using MapEditor.App.Controls;
using MapEditor.App.Dialogs;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App;

internal partial class MainWindow : Window
{
    private static readonly IBrush FieldErrorBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00));

    private readonly IEditorDialogs _dialogs;
    private readonly AppSettingsStore _settings;
    private readonly MainWindowViewModel _viewModel;
    private readonly AssetContextController _assets;
    private readonly MapCanvas _canvas;
    private readonly SpritePaletteControl _palette;
    private readonly TextBlock[] _layerRefs;
    private bool _closeGuardRunning;
    private bool _closeApproved;

    public MainWindow(IEditorDialogs dialogs, AppSettingsStore settings, MainWindowViewModel viewModel, AssetContextController assets)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        InitializeComponent();
        _canvas = new MapCanvas(_viewModel, _assets);
        _palette = new SpritePaletteControl(_viewModel, _assets);
        _layerRefs = new[] { Layer0Ref, Layer1Ref, Layer2Ref, Layer3Ref, Layer4Ref };
        CanvasHost.Child = _canvas;
        PaletteBorder.Child = _palette;
        _palette.BindScrollBar(PaletteBar);
        // TextChanged does not fire for programmatic Text sets, so validation tracks the property instead.
        BrushSheet.PropertyChanged += OnBrushFieldTextChanged;
        BrushGraphic.PropertyChanged += OnBrushFieldTextChanged;
        DataContext = _viewModel;
        Title = _viewModel.Title;
        ApplyHotKeys();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.CanvasInvalidated += SyncReadouts;
        SyncToolButtons();
        SyncLayerRadios();
        SyncBrushFields();
        SyncReadouts();
        SyncAssetDirectory();
        Closing += OnClosing;
        Opened += OnOpened;
    }

    internal MainWindowViewModel ViewModel => _viewModel;

    internal IEditorDialogs Dialogs => _dialogs;

    internal AppSettingsStore Settings => _settings;

    internal AssetContextController Assets => _assets;

    internal MapCanvas Canvas => _canvas;

    internal SpritePaletteControl Palette => _palette;

    internal static KeyGesture BuildShortcut(Key key, KeyModifiers extra, bool isMacOs)
        => new(key, extra | (isMacOs ? KeyModifiers.Meta : KeyModifiers.Control));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        KeyModifiers modifiers = e.KeyModifiers;
        if (IsPrimaryModifier(modifiers))
        {
            switch (e.Key)
            {
                case Key.N when modifiers == PrimaryModifier:
                    OnNew(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.O when modifiers == PrimaryModifier:
                    OnOpen(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.S when modifiers == PrimaryModifier:
                    OnSave(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.S when modifiers == (PrimaryModifier | KeyModifiers.Shift):
                    OnSaveAs(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.Z when modifiers == PrimaryModifier:
                    OnUndo(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.Z when modifiers == (PrimaryModifier | KeyModifiers.Shift):
                    OnRedo(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.Y when !OperatingSystem.IsMacOS() && modifiers == PrimaryModifier:
                    OnRedo(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }

            return;
        }

        if (modifiers != KeyModifiers.None)
        {
            return;
        }

        // Unmodified letters and +/- must reach a focused brush field (e.g. typing a negative number).
        if (e.Source is TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.P:
                _viewModel.ActiveTool = MapEditTool.Pencil;
                e.Handled = true;
                break;
            case Key.E:
                _viewModel.ActiveTool = MapEditTool.Eraser;
                e.Handled = true;
                break;
            case Key.I:
                _viewModel.ActiveTool = MapEditTool.Eyedropper;
                e.Handled = true;
                break;
            case Key.B:
                _viewModel.ActiveTool = MapEditTool.BlockedToggle;
                e.Handled = true;
                break;
            case Key.Add or Key.OemPlus:
                _canvas.ZoomStep(zoomIn: true);
                e.Handled = true;
                break;
            case Key.Subtract or Key.OemMinus:
                _canvas.ZoomStep(zoomIn: false);
                e.Handled = true;
                break;
        }
    }

    private static KeyModifiers PrimaryModifier => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    private static bool IsPrimaryModifier(KeyModifiers modifiers) => (modifiers & PrimaryModifier) != 0;

    private void ApplyHotKeys()
    {
        bool isMac = OperatingSystem.IsMacOS();
        NewCommand.HotKey = BuildShortcut(Key.N, KeyModifiers.None, isMac);
        OpenCommand.HotKey = BuildShortcut(Key.O, KeyModifiers.None, isMac);
        SaveCommand.HotKey = BuildShortcut(Key.S, KeyModifiers.None, isMac);
        SaveAsCommand.HotKey = BuildShortcut(Key.S, KeyModifiers.Shift, isMac);
        UndoCommand.HotKey = BuildShortcut(Key.Z, KeyModifiers.None, isMac);
        if (isMac)
        {
            RedoCommand.HotKey = BuildShortcut(Key.Z, KeyModifiers.Shift, isMac);
        }
        else
        {
            RedoCommand.HotKey = BuildShortcut(Key.Y, KeyModifiers.None, isMac);
        }
    }

    private void OnNew(object? sender, RoutedEventArgs e) => _ = RunCommandAsync(() => _viewModel.NewAsync());

    private void OnOpen(object? sender, RoutedEventArgs e) => _ = RunCommandAsync(() => _viewModel.OpenAsync());

    private void OnSave(object? sender, RoutedEventArgs e) => _ = RunCommandAsync(() => _viewModel.SaveAsync());

    private void OnSaveAs(object? sender, RoutedEventArgs e) => _ = RunCommandAsync(() => _viewModel.SaveAsAsync());

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        _canvas.FinishInteraction(commit: true);
        _viewModel.Undo();
    }

    private void OnRedo(object? sender, RoutedEventArgs e)
    {
        _canvas.FinishInteraction(commit: true);
        _viewModel.Redo();
    }

    private void OnToolChecked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string name } && Enum.TryParse(name, out MapEditTool tool))
        {
            _viewModel.ActiveTool = tool;
        }
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => _canvas.ZoomStep(zoomIn: true);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => _canvas.ZoomStep(zoomIn: false);

    private void OnLayerSelected(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, out int layer))
        {
            _viewModel.ActiveLayer = layer;
        }
    }

    private void OnBrushFieldTextChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
        {
            OnBrushFieldChanged();
        }
    }

    private void OnBrushFieldChanged()
    {
        bool sheetValid = int.TryParse(BrushSheet.Text, out int sheet);
        bool graphicValid = int.TryParse(BrushGraphic.Text, out int graphic);
        BrushSheet.BorderBrush = sheetValid ? null : FieldErrorBrush;
        BrushGraphic.BorderBrush = graphicValid ? null : FieldErrorBrush;
        BrushValidationError.IsVisible = !(sheetValid && graphicValid);
        if (!sheetValid || !graphicValid)
        {
            return;
        }

        MapTileLayer brush = new(sheet, graphic);
        if (_viewModel.Brush != brush)
        {
            _viewModel.Brush = brush;
        }
    }

    private async void OnLoadAssets(object? sender, RoutedEventArgs e)
    {
        string? picked = await _dialogs.PickAssetDirectoryAsync();
        if (picked is { } directory)
        {
            await TryOpenAssetsAsync(directory);
        }
    }

    private void OnOpened(object? sender, EventArgs e) => _ = InitializeAssetsAsync();

    private async Task InitializeAssetsAsync()
    {
        AppSettings settings;
        try
        {
            settings = _settings.Load();
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await ShowFatalErrorAsync("Settings", ex.Message);
            settings = new AppSettings(null);
        }

        if (settings.AssetDirectory is { } configured && await TryOpenAssetsAsync(configured))
        {
            return;
        }

        string? picked = await _dialogs.PickAssetDirectoryAsync();
        if (picked is { } directory)
        {
            await TryOpenAssetsAsync(directory);
        }
    }

    private async Task<bool> TryOpenAssetsAsync(string path)
    {
        if (_assets.TryOpen(path, out Exception? failure))
        {
            SyncAssetDirectory();
            return true;
        }

        await _dialogs.ShowErrorAsync(new ErrorPresentation("Load assets", failure is { } ex ? $"{path}: {ex.Message}" : path));
        return false;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _canvas.FinishInteraction(commit: true);
        if (_closeApproved)
        {
            return;
        }

        e.Cancel = true;
        if (_closeGuardRunning)
        {
            return;
        }

        _closeGuardRunning = true;
        bool approved;
        try
        {
            approved = await _viewModel.RequestCloseAsync();
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await ShowFatalErrorAsync("Close", ex.Message);
            approved = false;
        }
        finally
        {
            _closeGuardRunning = false;
        }

        if (approved)
        {
            _closeApproved = true;
            Close();
        }
    }

    private async Task RunCommandAsync(Func<Task> command)
    {
        _canvas.FinishInteraction(commit: true);
        try
        {
            await command();
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await ShowFatalErrorAsync("Error", ex.Message);
        }
    }

    private async Task ShowFatalErrorAsync(string title, string message)
    {
        try
        {
            await _dialogs.ShowErrorAsync(new ErrorPresentation(title, message));
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch
        {
            // The error dialog is the last resort; a failure there must not take the app down.
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.Title):
                Title = _viewModel.Title;
                break;
            case nameof(MainWindowViewModel.ActiveTool):
                SyncToolButtons();
                break;
            case nameof(MainWindowViewModel.ActiveLayer):
                SyncLayerRadios();
                break;
            case nameof(MainWindowViewModel.Brush):
                SyncBrushFields();
                break;
            case nameof(MainWindowViewModel.HoverX):
            case nameof(MainWindowViewModel.HoverY):
            case nameof(MainWindowViewModel.SelectedX):
            case nameof(MainWindowViewModel.SelectedY):
            case nameof(MainWindowViewModel.ZoomPercent):
            case nameof(MainWindowViewModel.MapWidth):
            case nameof(MainWindowViewModel.MapHeight):
                SyncReadouts();
                break;
        }
    }

    private void SyncToolButtons()
    {
        PencilTool.IsChecked = _viewModel.ActiveTool == MapEditTool.Pencil;
        EraserTool.IsChecked = _viewModel.ActiveTool == MapEditTool.Eraser;
        EyedropperTool.IsChecked = _viewModel.ActiveTool == MapEditTool.Eyedropper;
        BlockedTool.IsChecked = _viewModel.ActiveTool == MapEditTool.BlockedToggle;
    }

    private void SyncLayerRadios()
    {
        Layer0Radio.IsChecked = _viewModel.ActiveLayer == 0;
        Layer1Radio.IsChecked = _viewModel.ActiveLayer == 1;
        Layer2Radio.IsChecked = _viewModel.ActiveLayer == 2;
        Layer3Radio.IsChecked = _viewModel.ActiveLayer == 3;
        Layer4Radio.IsChecked = _viewModel.ActiveLayer == 4;
    }

    private void SyncBrushFields()
    {
        MapTileLayer brush = _viewModel.Brush;
        if (brush.Sheet.ToString() != BrushSheet.Text)
        {
            BrushSheet.Text = brush.Sheet.ToString();
        }

        if (brush.Graphic.ToString() != BrushGraphic.Text)
        {
            BrushGraphic.Text = brush.Graphic.ToString();
        }

        BrushValidationError.IsVisible = false;
        BrushSheet.BorderBrush = null;
        BrushGraphic.BorderBrush = null;
    }

    private void SyncReadouts()
    {
        HoverText.Text = _viewModel.HoverX is { } hoverX && _viewModel.HoverY is { } hoverY
            ? $"{hoverX}, {hoverY}"
            : "—";
        ZoomText.Text = $"{_viewModel.ZoomPercent}%";
        SizeText.Text = $"{_viewModel.MapWidth} × {_viewModel.MapHeight}";
        if (_viewModel.SelectedX is { } selectedX && _viewModel.SelectedY is { } selectedY &&
            selectedX >= 0 && selectedX < _viewModel.MapWidth &&
            selectedY >= 0 && selectedY < _viewModel.MapHeight)
        {
            MapTile tile = _viewModel.Session.Document[selectedX, selectedY];
            SelectedText.Text = $"{selectedX}, {selectedY}";
            BlockedText.Text = tile.IsBlocked ? "blocked: yes" : "blocked: no";
            for (int layer = 0; layer < MapDocument.LayerCount; layer++)
            {
                MapTileLayer tileLayer = tile.GetLayer(layer);
                _layerRefs[layer].Text = $"L{layer} {tileLayer.Sheet}/{tileLayer.Graphic}";
            }
        }
        else
        {
            SelectedText.Text = "—";
            BlockedText.Text = "blocked: no";
            for (int layer = 0; layer < MapDocument.LayerCount; layer++)
            {
                _layerRefs[layer].Text = $"L{layer} 0/0";
            }
        }
    }

    private void SyncAssetDirectory()
    {
        AssetDirectoryText.Text = _assets.Current.IsAvailable ? _assets.Current.Cache.AssetDirectory : "—";
    }
}
