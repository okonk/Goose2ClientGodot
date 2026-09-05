using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
    private readonly WorkspaceViewModel _workspace;
    private readonly AssetContextController _assets;
    private readonly INotifyCollectionChanged _documents;
    // The workspace owns the view models' lifetime; the window must never dispose them.
    private readonly Dictionary<MapDocumentViewModel, DocumentView> _views = new();
    private MapDocumentViewModel? _document;
    private readonly TextBlock[] _layerRefs;
    private Border[] _layerRows;
    private CheckBox[] _layerVisibleChecks;
    private AppTheme _theme = AppTheme.Dark;
    private bool _closeGuardRunning;
    private bool _closeApproved;
    // Breaks ActivateDocument -> SelectedItem -> SelectionChanged -> Activate re-entering itself.
    private bool _tabSelectionRunning;
    // Picker/settings continuations can resume after Closed; publishing then leaks an undisposed context.
    private bool _closed;

    private sealed record DocumentView(MapCanvas Canvas, SpritePaletteControl Palette);

    public MainWindow(IEditorDialogs dialogs, AppSettingsStore settings, WorkspaceViewModel workspace, AssetContextController assets)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        InitializeComponent();
        Body.ColumnDefinitions[0].Width = new GridLength(
            DefaultPaletteColumns * SpritePaletteControl.CellSize + PaletteChromeWidth, GridUnitType.Pixel);
        _layerRefs = new[] { Layer0Ref, Layer1Ref, Layer2Ref, Layer3Ref, Layer4Ref };
        _layerRows = new[] { Layer0Row, Layer1Row, Layer2Row, Layer3Row, Layer4Row };
        _layerVisibleChecks = new[] { Layer0VisibleCheck, Layer1VisibleCheck, Layer2VisibleCheck, Layer3VisibleCheck, Layer4VisibleCheck };
        AddHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
        // TextChanged does not fire for programmatic Text sets, so validation tracks the property instead.
        BrushSheet.PropertyChanged += OnBrushFieldTextChanged;
        BrushGraphic.PropertyChanged += OnBrushFieldTextChanged;
        // The async settings load reports failures; here a broken file just leaves the default theme.
        ApplyTheme(_settings.LoadOrDefault().Theme);
        ApplyHotKeys();
        _documents = _workspace.Documents;
        _documents.CollectionChanged += OnDocumentsChanged;
        _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        // The window's own DataContext is the active document, so the strip's items are set here.
        TabStrip.ItemsSource = _workspace.Documents;
        TabStrip.SelectionChanged += OnTabStripSelectionChanged;
        foreach (MapDocumentViewModel document in _workspace.Documents)
        {
            _views[document] = CreateView(document);
        }

        ActivateDocument(_workspace.ActiveDocument);
        Closing += OnClosing;
        Closed += (sender, e) =>
        {
            _closed = true;
            _assets.Dispose();
        };
        Opened += OnOpened;
    }

    internal WorkspaceViewModel Workspace => _workspace;

    internal MapCanvas Canvas => _views[_document!].Canvas;

    internal SpritePaletteControl Palette => _views[_document!].Palette;

    internal int LayerAnchor => _document!.LayerAnchor;

    internal IEditorDialogs Dialogs => _dialogs;

    internal AppSettingsStore Settings => _settings;

    internal AssetContextController Assets => _assets;

    internal bool HasViewFor(MapDocumentViewModel document) => _views.ContainsKey(document);

    private MapDocumentViewModel Document => _document!;

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The workspace only emits Add/Remove/Move, and Move carries the same document in both NewItems and
        // OldItems, so only Add/Remove change the set of hosted documents.
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (MapDocumentViewModel document in e.NewItems)
            {
                _views[document] = CreateView(document);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
        {
            foreach (MapDocumentViewModel document in e.OldItems)
            {
                DocumentView view = _views[document];
                if (ReferenceEquals(CanvasHost.Child, view.Canvas))
                {
                    CanvasHost.Child = null;
                }

                if (ReferenceEquals(PaletteBorder.Child, view.Palette))
                {
                    PaletteBorder.Child = null;
                }

                view.Palette.UnbindScrollBar();
                _views.Remove(document);
            }
        }
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.ActiveDocument))
        {
            ActivateDocument(_workspace.ActiveDocument);
        }
    }

    private void OnTabStripSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_tabSelectionRunning || TabStrip.SelectedItem is not MapDocumentViewModel document)
        {
            return;
        }

        _workspace.Activate(document);
    }

    // A press on the close button must not select the tab it is about to close.
    private void OnTabClosePressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void OnTabCloseClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MapDocumentViewModel document })
        {
            _ = RunCommandAsync(() => _workspace.CloseAsync(document));
        }
    }

    private void OnTabHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not StackPanel { DataContext: MapDocumentViewModel document } header ||
            !e.GetCurrentPoint(header).Properties.IsMiddleButtonPressed)
        {
            return;
        }

        e.Handled = true;
        _ = RunCommandAsync(() => _workspace.CloseAsync(document));
    }

    private void ActivateDocument(MapDocumentViewModel document)
    {
        if (ReferenceEquals(_document, document))
        {
            return;
        }

        DocumentView? outgoing = _document is { } current ? _views[current] : null;
        outgoing?.Canvas.FinishInteraction(commit: true);
        _document?.CancelPasteMode();
        if (_document is { } previous)
        {
            previous.PropertyChanged -= OnViewModelPropertyChanged;
            previous.CanvasInvalidated -= SyncReadouts;
        }

        document.PropertyChanged += OnViewModelPropertyChanged;
        document.CanvasInvalidated += SyncReadouts;
        _document = document;
        if (!_tabSelectionRunning)
        {
            _tabSelectionRunning = true;
            try
            {
                TabStrip.SelectedItem = document;
            }
            finally
            {
                _tabSelectionRunning = false;
            }
        }

        TabStrip.ScrollIntoView(document);
        DocumentView view = _views[document];
        DataContext = document;
        CanvasHost.Child = view.Canvas;
        PaletteBorder.Child = view.Palette;
        outgoing?.Palette.UnbindScrollBar();
        view.Palette.BindScrollBar(PaletteBar);
        SyncToolButtons();
        SyncLayerRows();
        SyncBrushFields();
        SyncReadouts();
        SyncAssetDirectory();
        Title = document.Title;
    }

    internal static KeyGesture BuildShortcut(Key key, KeyModifiers extra, bool isMacOs)
        => new(key, extra | (isMacOs ? KeyModifiers.Meta : KeyModifiers.Control));

    private DocumentView CreateView(MapDocumentViewModel document)
        => new(new MapCanvas(document, _assets), new SpritePaletteControl(document, _assets));

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
                    Document.CopySelection();
                    e.Handled = true;
                    break;
                case Key.V when modifiers == PrimaryModifier && e.Source is not TextBox:
                    Document.BeginPasteMode();
                    e.Handled = true;
                    break;
                case Key.X when modifiers == PrimaryModifier && e.Source is not TextBox:
                    Document.CutSelection();
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
            Canvas.FinishInteraction(commit: false);
            Document.CancelPasteMode();
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
                Document.ActiveTool = MapEditTool.Pencil;
                e.Handled = true;
                break;
            case Key.E:
                Document.ActiveTool = MapEditTool.Eraser;
                e.Handled = true;
                break;
            case Key.I:
                Document.ActiveTool = MapEditTool.Eyedropper;
                e.Handled = true;
                break;
            case Key.X:
                Document.ActiveTool = MapEditTool.Blocked;
                e.Handled = true;
                break;
            case Key.V:
                Document.ActiveTool = MapEditTool.Select;
                e.Handled = true;
                break;
            case Key.M:
                Document.ActiveTool = MapEditTool.MultiSelect;
                e.Handled = true;
                break;
            case Key.B:
                Document.ActiveTool = MapEditTool.FloodFill;
                e.Handled = true;
                break;
            case Key.Delete:
                Document.DeleteSelection();
                e.Handled = true;
                break;
            case Key.Add or Key.OemPlus:
                Canvas.ZoomStep(zoomIn: true);
                e.Handled = true;
                break;
            case Key.Subtract or Key.OemMinus:
                Canvas.ZoomStep(zoomIn: false);
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

    private void OnResize(object? sender, RoutedEventArgs e)
        => _ = RunCommandAsync(async () =>
        {
            MapTileRectangle? window = await _dialogs.ShowResizeMapAsync(Document.Session.Document);
            if (window is { } value)
            {
                Document.ResizeMap(value);
            }
        });

    private void OnNew(object? sender, RoutedEventArgs e) => _ = RunCommandAsync(() => _workspace.NewAsync());

    private void OnOpen(object? sender, RoutedEventArgs e) => _ = RunCommandAsync(() => _workspace.OpenAsync());

    private void OnSave(object? sender, RoutedEventArgs e) => _ = RunCommandAsync(() => Document.SaveAsync());

    private void OnSaveAs(object? sender, RoutedEventArgs e) => _ = RunCommandAsync(() => Document.SaveAsAsync());

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        Canvas.FinishInteraction(commit: true);
        Document.Undo();
    }

    private void OnRedo(object? sender, RoutedEventArgs e)
    {
        Canvas.FinishInteraction(commit: true);
        Document.Redo();
    }

    private void OnToolChecked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string name } && Enum.TryParse(name, out MapEditTool tool))
        {
            Document.ActiveTool = tool;
        }
    }

    private void OnToolUnchecked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } &&
            Enum.TryParse<MapEditTool>(tag, out MapEditTool tool) &&
            tool == Document.ActiveTool)
        {
            ((ToggleButton)sender).IsChecked = true;
        }
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Document.PasteMode && e.Source is not MapCanvas)
        {
            Document.CancelPasteMode();
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

        (Document.SelectedLayers, Document.LayerAnchor) = LayerSelection.Apply(Document.SelectedLayers, Document.LayerAnchor, layer, mode);
        e.Handled = true;
    }

    private void OnLayerVisibilityChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string tag } && int.TryParse(tag, out int layer))
        {
            byte mask = Document.LayerVisibility;
            mask = (byte)(mask & ~(1 << layer));
            if (sender is CheckBox { IsChecked: true })
            {
                mask |= (byte)(1 << layer);
            }

            Document.LayerVisibility = mask;
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
        if (Document.Brush != brush)
        {
            Document.Brush = brush;
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
        Canvas.FinishInteraction(commit: true);
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
            approved = await _workspace.CloseAllAsync();
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
        Canvas.FinishInteraction(commit: true);
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
            case nameof(MapDocumentViewModel.Title):
                Title = Document.Title;
                break;
            case nameof(MapDocumentViewModel.ActiveTool):
                SyncToolButtons();
                break;
            case nameof(MapDocumentViewModel.SelectedLayers):
                if ((Document.SelectedLayers & (1 << Document.LayerAnchor)) == 0)
                {
                    Document.LayerAnchor = Document.TopLayer;
                }
                SyncLayerRows();
                break;
            case nameof(MapDocumentViewModel.LayerVisibility):
                SyncLayerRows();
                break;
            case nameof(MapDocumentViewModel.Brush):
                SyncBrushFields();
                break;
            case nameof(MapDocumentViewModel.HoverX):
            case nameof(MapDocumentViewModel.HoverY):
            case nameof(MapDocumentViewModel.SelectedX):
            case nameof(MapDocumentViewModel.SelectedY):
            case nameof(MapDocumentViewModel.ZoomPercent):
            case nameof(MapDocumentViewModel.MapWidth):
            case nameof(MapDocumentViewModel.MapHeight):
                SyncReadouts();
                break;
        }
    }

    private void SyncToolButtons()
    {
        PencilTool.IsChecked = Document.ActiveTool == MapEditTool.Pencil;
        EraserTool.IsChecked = Document.ActiveTool == MapEditTool.Eraser;
        EyedropperTool.IsChecked = Document.ActiveTool == MapEditTool.Eyedropper;
        BlockedTool.IsChecked = Document.ActiveTool == MapEditTool.Blocked;
        SelectTool.IsChecked = Document.ActiveTool == MapEditTool.Select;
        MultiSelectTool.IsChecked = Document.ActiveTool == MapEditTool.MultiSelect;
        FloodFillTool.IsChecked = Document.ActiveTool == MapEditTool.FloodFill;
    }

    private void SyncLayerRows()
    {
        byte selection = Document.SelectedLayers;
        byte visibility = Document.LayerVisibility;
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            _layerRows[layer].Background = (selection & (1 << layer)) != 0 ? SelectedRowBrush : Brushes.Transparent;
            _layerVisibleChecks[layer].IsChecked = (visibility & (1 << layer)) != 0;
        }
    }

    private void SyncBrushFields()
    {
        MapTileLayer brush = Document.Brush;
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
        HoverText.Text = Document.HoverX is { } hoverX && Document.HoverY is { } hoverY
            ? $"{hoverX}, {hoverY}"
            : "—";
        ZoomText.Text = $"{Document.ZoomPercent}%";
        SizeText.Text = $"{Document.MapWidth} × {Document.MapHeight}";
        if (Document.SelectedX is { } selectedX && Document.SelectedY is { } selectedY &&
            selectedX >= 0 && selectedX < Document.MapWidth &&
            selectedY >= 0 && selectedY < Document.MapHeight)
        {
            MapTile tile = Document.Session.Document[selectedX, selectedY];
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
