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
using Avalonia.Styling;
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
    private const int DefaultPaletteColumns = 10;
    // PaletteHost margin (24) + palette border (2); the scrollbar overlays the grid.
    private const double PaletteChromeWidth = 26;

    // Readable against both the light and the dark field background.
    private static readonly IBrush FieldErrorBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x55, 0x55));
    private static readonly IBrush SelectedRowBrush = new SolidColorBrush(Color.FromArgb(0x59, 0x4C, 0x8D, 0xFF));

    private readonly IEditorDialogs _dialogs;
    private readonly AppSettingsStore _settings;
    private readonly MainWindowViewModel _viewModel;
    private readonly AssetContextController _assets;
    private readonly MapCanvas _canvas;
    private readonly SpritePaletteControl _palette;
    private readonly TextBlock[] _layerRefs;
    private Border[] _layerRows;
    private CheckBox[] _layerVisibleChecks;
    private int _layerAnchor;
    private AppTheme _theme = AppTheme.Dark;
    private bool _closeGuardRunning;
    private bool _closeApproved;
    // Picker/settings continuations can resume after Closed; publishing then leaks an undisposed context.
    private bool _closed;

    public MainWindow(IEditorDialogs dialogs, AppSettingsStore settings, MainWindowViewModel viewModel, AssetContextController assets)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        InitializeComponent();
        Body.ColumnDefinitions[0].Width = new GridLength(
            DefaultPaletteColumns * SpritePaletteControl.CellSize + PaletteChromeWidth, GridUnitType.Pixel);
        _canvas = new MapCanvas(_viewModel, _assets);
        _palette = new SpritePaletteControl(_viewModel, _assets);
        _layerRefs = new[] { Layer0Ref, Layer1Ref, Layer2Ref, Layer3Ref, Layer4Ref };
        _layerRows = new[] { Layer0Row, Layer1Row, Layer2Row, Layer3Row, Layer4Row };
        _layerVisibleChecks = new[] { Layer0VisibleCheck, Layer1VisibleCheck, Layer2VisibleCheck, Layer3VisibleCheck, Layer4VisibleCheck };
        CanvasHost.Child = _canvas;
        PaletteBorder.Child = _palette;
        _palette.BindScrollBar(PaletteBar);
        AddHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
        // TextChanged does not fire for programmatic Text sets, so validation tracks the property instead.
        BrushSheet.PropertyChanged += OnBrushFieldTextChanged;
        BrushGraphic.PropertyChanged += OnBrushFieldTextChanged;
        DataContext = _viewModel;
        Title = _viewModel.Title;
        // The async settings load reports failures; here a broken file just leaves the default theme.
        ApplyTheme(_settings.LoadOrDefault().Theme);
        ApplyHotKeys();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.CanvasInvalidated += SyncReadouts;
        SyncToolButtons();
        SyncLayerRows();
        SyncBrushFields();
        SyncReadouts();
        SyncAssetDirectory();
        Closing += OnClosing;
        Closed += (sender, e) =>
        {
            _closed = true;
            _assets.Dispose();
        };
        Opened += OnOpened;
    }

    internal MainWindowViewModel ViewModel => _viewModel;

    internal IEditorDialogs Dialogs => _dialogs;

    internal AppSettingsStore Settings => _settings;

    internal AssetContextController Assets => _assets;

    internal MapCanvas Canvas => _canvas;

    internal SpritePaletteControl Palette => _palette;

    internal int LayerAnchor => _layerAnchor;

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
                case Key.C when modifiers == PrimaryModifier && e.Source is not TextBox:
                    _viewModel.CopySelection();
                    e.Handled = true;
                    break;
                case Key.V when modifiers == PrimaryModifier && e.Source is not TextBox:
                    _viewModel.BeginPasteMode();
                    e.Handled = true;
                    break;
            }

            return;
        }

        if (modifiers != KeyModifiers.None)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            _canvas.FinishInteraction(commit: false);
            _viewModel.CancelPasteMode();
            e.Handled = true;
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
            case Key.X:
                _viewModel.ActiveTool = MapEditTool.Blocked;
                e.Handled = true;
                break;
            case Key.V:
                _viewModel.ActiveTool = MapEditTool.Select;
                e.Handled = true;
                break;
            case Key.M:
                _viewModel.ActiveTool = MapEditTool.MultiSelect;
                e.Handled = true;
                break;
            case Key.B:
                _viewModel.ActiveTool = MapEditTool.FloodFill;
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

    private void OnToolUnchecked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } &&
            Enum.TryParse<MapEditTool>(tag, out MapEditTool tool) &&
            tool == _viewModel.ActiveTool)
        {
            ((ToggleButton)sender).IsChecked = true;
        }
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel.PasteMode && e.Source is not MapCanvas)
        {
            _viewModel.CancelPasteMode();
        }
    }

    private void OnLayerRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: string tag } || !int.TryParse(tag, out int layer))
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        KeyModifiers modifiers = e.KeyModifiers;
        LayerClickMode mode = modifiers.HasFlag(KeyModifiers.Control)
            ? LayerClickMode.Toggle
            : modifiers.HasFlag(KeyModifiers.Shift)
                ? LayerClickMode.Range
                : LayerClickMode.Plain;

        (_viewModel.SelectedLayers, _layerAnchor) = LayerSelection.Apply(_viewModel.SelectedLayers, _layerAnchor, layer, mode);
        e.Handled = true;
    }

    private void OnLayerVisibilityChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string tag } && int.TryParse(tag, out int layer))
        {
            byte mask = _viewModel.LayerVisibility;
            mask = (byte)(mask & ~(1 << layer));
            if (sender is CheckBox { IsChecked: true })
            {
                mask |= (byte)(1 << layer);
            }

            _viewModel.LayerVisibility = mask;
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

    private void OnLoadAssets(object? sender, RoutedEventArgs e)
        => _ = RunCommandAsync(async () =>
        {
            string? picked = await _dialogs.PickAssetDirectoryAsync();
            if (picked is { } directory)
            {
                await TryOpenAssetsAsync(directory);
            }
        });

    private void OnThemeSelected(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse(tag, out AppTheme theme))
        {
            return;
        }

        if (theme == _theme)
        {
            SyncThemeMenu();
            return;
        }

        ApplyTheme(theme);
        try
        {
            _settings.Update(current => current with { Theme = theme });
        }
        catch (AppSettingsException)
        {
            // A settings file that cannot be written must not break the running session.
        }
    }

    private void ApplyTheme(AppTheme theme)
    {
        _theme = theme;
        ThemeVariant variant = theme == AppTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        RequestedThemeVariant = variant;
        // Dialogs are separate top-level windows, so the variant has to reach the application too.
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = variant;
        }

        SyncThemeMenu();
    }

    private void SyncThemeMenu()
    {
        DarkThemeMenuItem.IsChecked = _theme == AppTheme.Dark;
        LightThemeMenuItem.IsChecked = _theme == AppTheme.Light;
    }

    private void OnOpened(object? sender, EventArgs e) => _ = RunCommandAsync(InitializeAssetsAsync);

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
        if (_closed)
        {
            return false;
        }

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
            case nameof(MainWindowViewModel.SelectedLayers):
                if ((_viewModel.SelectedLayers & (1 << _layerAnchor)) == 0)
                {
                    _layerAnchor = _viewModel.TopLayer;
                }
                SyncLayerRows();
                break;
            case nameof(MainWindowViewModel.LayerVisibility):
                SyncLayerRows();
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
        BlockedTool.IsChecked = _viewModel.ActiveTool == MapEditTool.Blocked;
        SelectTool.IsChecked = _viewModel.ActiveTool == MapEditTool.Select;
        MultiSelectTool.IsChecked = _viewModel.ActiveTool == MapEditTool.MultiSelect;
        FloodFillTool.IsChecked = _viewModel.ActiveTool == MapEditTool.FloodFill;
    }

    private void SyncLayerRows()
    {
        byte selection = _viewModel.SelectedLayers;
        byte visibility = _viewModel.LayerVisibility;
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            _layerRows[layer].Background = (selection & (1 << layer)) != 0 ? SelectedRowBrush : Brushes.Transparent;
            _layerVisibleChecks[layer].IsChecked = (visibility & (1 << layer)) != 0;
        }
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
