using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MapEditor.App.Controls;
using MapEditor.App.Dialogs;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Rows;
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
    private MapDocumentViewModel? _dragDocument;
    private IPointer? _dragPointer;
    private int _dragIndex;
    private int? _spawnPrefillSelection;
    private int? _warpPrefillSelection;
    private AppTheme _theme = AppTheme.Dark;
    private bool _commandRunning;
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
        SpawnNpcPicker.ItemText = npc => $"{npc.NpcId}  {npc.NpcName}";
        WarpDestinationPicker.ItemText = map => $"{map.MapId}  {map.MapName}  {map.MapFilename}";
        AddHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerMovedEvent, OnWindowPointerMoved);
        AddHandler(InputElement.PointerReleasedEvent, OnWindowPointerReleased);
        AddHandler(InputElement.PointerCaptureLostEvent, OnWindowPointerCaptureLost);
        // Tunnel so tab shortcuts win over the window's Tab focus navigation, which would otherwise
        // consume Ctrl+Tab before any bubbling handler sees it.
        AddHandler(InputElement.KeyDownEvent, OnTabShortcutKeyDown, RoutingStrategies.Tunnel);
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
            if (e.OldItems.Contains(_dragDocument))
            {
                ClearTabDrag();
            }

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

        if (_commandRunning)
        {
            // The strip already moved its selection on the press; restore agreement with the workspace.
            SelectTabInStrip(_workspace.ActiveDocument);
            return;
        }

        _workspace.Activate(document);
    }

    // A press on the close button must not select the tab it is about to close.
    private void OnTabClosePressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: MapDocumentViewModel document } button &&
            e.GetCurrentPoint(button).Properties.IsMiddleButtonPressed)
        {
            _ = RunCommandAsync(() => _workspace.CloseAsync(document));
        }
    }

    private void OnTabCloseClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MapDocumentViewModel document })
        {
            _ = RunCommandAsync(() => _workspace.CloseAsync(document));
        }
    }

    private void OnTabHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not StackPanel { DataContext: MapDocumentViewModel document } header)
        {
            return;
        }

        PointerPointProperties properties = e.GetCurrentPoint(header).Properties;
        if (properties.IsMiddleButtonPressed)
        {
            e.Handled = true;
            _ = RunCommandAsync(() => _workspace.CloseAsync(document));
            return;
        }

        if (!properties.IsLeftButtonPressed || PressedInsideButton(e.Source as Visual))
        {
            return;
        }

        _dragDocument = document;
        _dragPointer = e.Pointer;
        _dragIndex = _workspace.Documents.IndexOf(document);
        e.Pointer.Capture(header);
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragDocument is not { } document || !ReferenceEquals(_dragPointer, e.Pointer))
        {
            return;
        }

        if (!_workspace.Documents.Contains(document))
        {
            ClearTabDrag();
            return;
        }

        // A Move recreates the tab containers, so the header must be re-resolved on every move.
        if (GetTabHeader(document) is not { } header)
        {
            return;
        }

        int index = _workspace.Documents.IndexOf(document);
        Point position = e.GetPosition(header);
        if (position.Y < 0 || position.Y > header.Bounds.Height)
        {
            return;
        }

        if (index > 0 &&
            GetTabHeader(_workspace.Documents[index - 1]) is { } left &&
            left.TranslatePoint(new Point(left.Bounds.Width / 2, 0), header) is { } leftMid &&
            position.X < leftMid.X)
        {
            _workspace.Move(index, index - 1);
            return;
        }

        if (index + 1 < _workspace.Documents.Count &&
            GetTabHeader(_workspace.Documents[index + 1]) is { } right &&
            right.TranslatePoint(new Point(right.Bounds.Width / 2, 0), header) is { } rightMid &&
            position.X > rightMid.X)
        {
            _workspace.Move(index, index + 1);
        }
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragDocument is null)
        {
            return;
        }

        if (ReferenceEquals(_dragPointer, e.Pointer))
        {
            e.Pointer.Capture(null);
            ClearTabDrag();
        }
    }

    private void OnWindowPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_dragDocument is not null && ReferenceEquals(_dragPointer, e.Pointer))
        {
            ClearTabDrag();
        }
    }

    private void ClearTabDrag()
    {
        _dragDocument = null;
        _dragPointer = null;
    }

    private StackPanel? GetTabHeader(MapDocumentViewModel document)
        => TabStrip.ContainerFromItem(document) is ListBoxItem { } item
            ? item.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(panel => panel.Classes.Contains("tabHeader"))
            : null;

    private static bool PressedInsideButton(Visual? source)
    {
        while (source is { } visual)
        {
            if (visual is Button)
            {
                return true;
            }

            source = visual.GetVisualParent();
        }

        return false;
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
            previous.GameData?.Changed -= OnGameDataStateChanged;
        }

        document.PropertyChanged += OnViewModelPropertyChanged;
        document.CanvasInvalidated += SyncReadouts;
        document.GameData?.Changed += OnGameDataStateChanged;
        _document = document;
        if (!_tabSelectionRunning)
        {
            SelectTabInStrip(document);
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
        _spawnPrefillSelection = null;
        _warpPrefillSelection = null;
        SyncGameDataChrome();
        SyncRightPanel();
        Title = document.Title;
    }

    private void SelectTabInStrip(MapDocumentViewModel document)
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

        if (e.Key == Key.Escape && _dragDocument is not null)
        {
            int index = _workspace.Documents.IndexOf(_dragDocument!);
            if (index != _dragIndex)
            {
                _workspace.Move(index, _dragIndex);
            }

            _dragPointer?.Capture(null);
            ClearTabDrag();
            e.Handled = true;
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

    private void OnTabShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsPrimaryModifier(e.KeyModifiers))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.T when e.KeyModifiers == PrimaryModifier:
                _ = RunCommandAsync(() => _workspace.NewAsync());
                e.Handled = true;
                break;
            case Key.W when e.KeyModifiers == PrimaryModifier:
                _ = RunCommandAsync(() => _workspace.CloseAsync(_document!));
                e.Handled = true;
                break;
            case Key.Tab when e.KeyModifiers == (PrimaryModifier | KeyModifiers.Shift):
                CycleTab(-1);
                e.Handled = true;
                break;
            case Key.Tab when e.KeyModifiers == PrimaryModifier:
                CycleTab(1);
                e.Handled = true;
                break;
            case Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5 or Key.D6 or Key.D7 or Key.D8
                 or Key.NumPad1 or Key.NumPad2 or Key.NumPad3 or Key.NumPad4 or Key.NumPad5
                 or Key.NumPad6 or Key.NumPad7 or Key.NumPad8 when e.KeyModifiers == PrimaryModifier:
                ActivateTabByIndex(DigitIndex(e.Key));
                e.Handled = true;
                break;
            case Key.D9 or Key.NumPad9 when e.KeyModifiers == PrimaryModifier:
                ActivateTabByIndex(_workspace.Documents.Count);
                e.Handled = true;
                break;
        }
    }

    // D1–D8 and NumPad1–8 are contiguous in Avalonia's Key enum, with the NumPad values above D8.
    private static int DigitIndex(Key key)
        => key <= Key.D8 ? key - Key.D1 + 1 : key - Key.NumPad1 + 1;

    private void CycleTab(int direction)
    {
        if (_commandRunning)
        {
            return;
        }

        var documents = _workspace.Documents;
        if (documents.Count < 2)
        {
            return;
        }

        int index = documents.IndexOf(_document!);
        _workspace.Activate(documents[(index + direction + documents.Count) % documents.Count]);
    }

    private void ActivateTabByIndex(int oneBasedIndex)
    {
        if (_commandRunning)
        {
            return;
        }

        var documents = _workspace.Documents;
        if (oneBasedIndex < 1 || oneBasedIndex > documents.Count)
        {
            return;
        }

        _workspace.Activate(documents[oneBasedIndex - 1]);
    }

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
            Document.GameData!.ActiveTool == GameDataTool.None &&
            Enum.TryParse<MapEditTool>(tag, out MapEditTool tool) &&
            tool == Document.ActiveTool)
        {
            ((ToggleButton)sender).IsChecked = true;
        }
    }

    private void OnGameToolChecked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string name } && Enum.TryParse(name, out GameDataTool tool))
        {
            Document.GameData!.ActiveTool = tool;
        }
    }

    private void OnGameToolUnchecked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string name } &&
            Enum.TryParse(name, out GameDataTool tool) &&
            Document.GameData!.ActiveTool == tool)
        {
            ((ToggleButton)sender).IsChecked = true;
        }
    }

    private void OnConnect(object? sender, RoutedEventArgs e)
        => _ = RunCommandAsync(async () =>
        {
            if (_workspace.Commands.IsConnected)
            {
                await _workspace.Commands.DisconnectAsync();
            }
            else
            {
                await _workspace.Commands.ConnectAsync();
            }
        });

    private void OnPull(object? sender, RoutedEventArgs e)
        => _ = RunCommandAsync(() => _workspace.Commands.PullAsync(Document));

    private void OnPush(object? sender, RoutedEventArgs e)
        => _ = RunCommandAsync(() => _workspace.Commands.PushAsync(Document));

    private void OnUseSelectedTile(object? sender, RoutedEventArgs e)
    {
        if (Document.SelectedX is { } x && Document.SelectedY is { } y)
        {
            WarpSourceX.Text = x.ToString();
            WarpSourceY.Text = y.ToString();
        }
    }

    private void OnSpawnDelete(object? sender, RoutedEventArgs e)
    {
        if (Document.GameData is { SelectedSpawn: { } index, Session: { } session } state && index < session.Edits.Spawns.Count)
        {
            session.Edits.RemoveSpawnAt(index);
            state.SelectedSpawn = null;
        }
    }

    private void OnWarpDelete(object? sender, RoutedEventArgs e)
    {
        if (Document.GameData is { SelectedWarp: { } index, Session: { } session } state && index < session.Edits.Warps.Count)
        {
            session.Edits.RemoveWarpAt(index);
            state.SelectedWarp = null;
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
        // A modal command is on screen; its continuation would resume after Closed, so the close must wait.
        if (_commandRunning)
        {
            e.Cancel = true;
            return;
        }

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
        if (_commandRunning)
        {
            return;
        }

        _commandRunning = true;
        try
        {
            Canvas.FinishInteraction(commit: true);
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
        finally
        {
            _commandRunning = false;
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
        GameDataTool gameTool = Document.GameData!.ActiveTool;
        PencilTool.IsChecked = gameTool == GameDataTool.None && Document.ActiveTool == MapEditTool.Pencil;
        EraserTool.IsChecked = gameTool == GameDataTool.None && Document.ActiveTool == MapEditTool.Eraser;
        EyedropperTool.IsChecked = gameTool == GameDataTool.None && Document.ActiveTool == MapEditTool.Eyedropper;
        BlockedTool.IsChecked = gameTool == GameDataTool.None && Document.ActiveTool == MapEditTool.Blocked;
        SelectTool.IsChecked = gameTool == GameDataTool.None && Document.ActiveTool == MapEditTool.Select;
        MultiSelectTool.IsChecked = gameTool == GameDataTool.None && Document.ActiveTool == MapEditTool.MultiSelect;
        FloodFillTool.IsChecked = gameTool == GameDataTool.None && Document.ActiveTool == MapEditTool.FloodFill;
        SpawnTool.IsChecked = gameTool == GameDataTool.Spawn;
        WarpTool.IsChecked = gameTool == GameDataTool.Warp;
    }

    private void OnGameDataStateChanged()
    {
        SyncToolButtons();
        SyncRightPanel();
        SyncGameDataChrome();
    }

    private void SyncGameDataChrome()
    {
        ConnectCommand.Header = _workspace.Commands.IsConnected ? "Disconnect" : "Connect";
        PullCommand.IsEnabled = _workspace.Commands.CanPull;
        PushCommand.IsEnabled = _workspace.Commands.CanPush(Document);
    }

    private void SyncRightPanel()
    {
        DocumentGameDataState state = Document.GameData!;
        bool showSpawn = state.ActiveTool == GameDataTool.Spawn || state.SelectedSpawn is not null;
        bool showWarp = state.ActiveTool == GameDataTool.Warp || state.SelectedWarp is not null;
        bool spawnWasVisible = SpawnProperties.IsVisible;
        bool warpWasVisible = WarpProperties.IsVisible;
        RightPanel.IsVisible = !showSpawn && !showWarp;
        SpawnProperties.IsVisible = showSpawn;
        WarpProperties.IsVisible = showWarp;
        SpawnNpcPicker.Items = state.Session?.Npcs.Values.OrderBy(npc => npc.NpcId).ToList() ?? new List<NpcAppearance>();
        WarpDestinationPicker.Items = state.Session?.Maps ?? Array.Empty<MapReference>();
        if (!showSpawn)
        {
            _spawnPrefillSelection = null;
        }

        if (!showWarp)
        {
            _warpPrefillSelection = null;
        }

        if (showSpawn)
        {
            SyncSpawnProperties(state, spawnWasVisible);
        }

        if (showWarp)
        {
            SyncWarpProperties(state, warpWasVisible);
        }
    }

    private void SyncSpawnProperties(DocumentGameDataState state, bool wasVisible)
    {
        SpawnDeleteButton.IsEnabled = state.SelectedSpawn is not null;
        if (!wasVisible || state.SelectedSpawn != _spawnPrefillSelection)
        {
            if (state.SelectedSpawn is { } index && state.Session is { } session && index < session.Edits.Spawns.Count)
            {
                _spawnPrefillSelection = index;
                NpcSpawnRow spawn = session.Edits.Spawns[index];
                SpawnSourceX.Text = spawn.MapX.ToString();
                SpawnSourceY.Text = spawn.MapY.ToString();
            }
        }
    }

    private void SyncWarpProperties(DocumentGameDataState state, bool wasVisible)
    {
        WarpDeleteButton.IsEnabled = state.SelectedWarp is not null;
        if (!wasVisible || state.SelectedWarp != _warpPrefillSelection)
        {
            if (state.SelectedWarp is { } index && state.Session is { } session && index < session.Edits.Warps.Count)
            {
                _warpPrefillSelection = index;
                WarpRow warp = session.Edits.Warps[index];
                WarpSourceX.Text = warp.MapX.ToString();
                WarpSourceY.Text = warp.MapY.ToString();
                WarpDestinationX.Text = warp.WarpX.ToString();
                WarpDestinationY.Text = warp.WarpY.ToString();
            }
        }
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
        WarpUseSelectedTileButton.IsEnabled = Document.SelectedX is not null && Document.SelectedY is not null;
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
