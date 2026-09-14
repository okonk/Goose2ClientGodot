using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MapEditor.App.Controls;
using MapEditor.App.Dialogs;
using MapEditor.App.Rendering;
using MapEditor.App.Terrain;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App;

internal partial class TerrainEditorWindow : Window
{
    private static readonly double[] ZoomOptions = { 0.25, 0.5, 1.0, 2.0, 4.0, 8.0 };

    private readonly TerrainEditorController _controller;
    private readonly IEditorDialogs _dialogs;
    private readonly ISpriteSheetLoader _loader;
    private TerrainEditorViewModel _viewModel = null!;
    private TerrainSheetControl _sheetControl = null!;
    private string _sheetDirectory = string.Empty;
    private bool _syncingSelection;
    private bool _closeApproved;
    private bool _closeGuardRunning;
    private bool _closed;

    public TerrainEditorWindow(TerrainEditorController controller, IEditorDialogs dialogs, AssetContext context)
        : this(controller, dialogs, context, new AvaloniaSpriteSheetLoader())
    {
    }

    internal TerrainEditorWindow(TerrainEditorController controller, IEditorDialogs dialogs, AssetContext context, ISpriteSheetLoader loader)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        ArgumentNullException.ThrowIfNull(context);
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        InitializeComponent();

        ZoomCombo.ItemsSource = ZoomOptions;
        SheetCombo.SelectionChanged += OnSheetSelectionChanged;
        ZoomCombo.SelectionChanged += OnZoomSelectionChanged;
        NameBox.TextChanged += OnNameTextChanged;
        ColorBox.TextChanged += OnColorTextChanged;
        TerrainList.SelectionChanged += OnTerrainSelectionChanged;
        _controller.StateChanged += OnControllerStateChanged;
        BindViewModel(_controller.ViewModel);
        SyncRecoveryPanel();
        Closed += (sender, e) => _closed = true;
    }

    internal TerrainEditorViewModel ViewModel => _viewModel;

    internal TerrainSheetControl SheetControl => _sheetControl;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // ComboBox selection set before the template is applied does not stick.
        SheetCombo.SelectedItem = _viewModel.SelectedSheet is int sheet ? sheet : null;
        ZoomCombo.SelectedItem = _viewModel.Zoom;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Source is TextBox)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Z)
        {
            _viewModel.Undo();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Y)
        {
            _viewModel.Redo();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.Z)
        {
            _viewModel.Redo();
            e.Handled = true;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeApproved)
        {
            return;
        }

        if (_closeGuardRunning)
        {
            e.Cancel = true;
            return;
        }

        _closeGuardRunning = true;
        _ = HandleClosingAsync(e);
    }

    private void BindViewModel(TerrainEditorViewModel viewModel)
    {
        if (!ReferenceEquals(_viewModel, viewModel))
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.SheetInvalidated -= OnSheetInvalidated;
            }

            _viewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.SheetInvalidated += OnSheetInvalidated;
            ReplaceSheetControl();
        }

        _syncingSelection = true;
        TerrainList.ItemsSource = viewModel.Terrains;
        TerrainList.SelectedItem = viewModel.SelectedTerrain;
        _syncingSelection = false;
        SheetCombo.ItemsSource = viewModel.EligibleSheets;
        SheetCombo.SelectedItem = viewModel.SelectedSheet is int sheet ? sheet : null;
        SyncFields();
        SyncErrors();
        SyncValidation();
        SyncCommandState();
    }

    private void ReplaceSheetControl()
    {
        if (_sheetControl is not null)
        {
            _sheetControl.Images.ImageChanged -= OnSheetImageChanged;
            SheetHost.Child = null;
            _sheetControl.Dispose();
        }

        _sheetDirectory = _controller.Context.Cache.AssetDirectory;
        _sheetControl = new TerrainSheetControl(_viewModel, _sheetDirectory, _loader);
        SheetHost.Child = _sheetControl;
        _sheetControl.Images.ImageChanged += OnSheetImageChanged;
        SyncSheetDiagnostic();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TerrainEditorViewModel.Terrains):
                _syncingSelection = true;
                TerrainList.ItemsSource = _viewModel.Terrains;
                TerrainList.SelectedItem = _viewModel.SelectedTerrain;
                _syncingSelection = false;
                break;
            case nameof(TerrainEditorViewModel.SelectedTerrain):
                SyncSelection();
                break;
            case nameof(TerrainEditorViewModel.Name):
                NameBox.Text = _viewModel.Name;
                break;
            case nameof(TerrainEditorViewModel.ColorOverrideText):
                ColorBox.Text = _viewModel.ColorOverrideText;
                break;
            case nameof(TerrainEditorViewModel.NameError):
            case nameof(TerrainEditorViewModel.ColorError):
                SyncErrors();
                break;
            case nameof(TerrainEditorViewModel.SelectedSheet):
                SheetCombo.SelectedItem = _viewModel.SelectedSheet is int sheet ? sheet : null;
                break;
            case nameof(TerrainEditorViewModel.Zoom):
                ZoomCombo.SelectedItem = _viewModel.Zoom;
                break;
            case nameof(TerrainEditorViewModel.Errors):
            case nameof(TerrainEditorViewModel.Warnings):
                SyncValidation();
                break;
            case nameof(TerrainEditorViewModel.IsDirty):
            case nameof(TerrainEditorViewModel.CanUndo):
            case nameof(TerrainEditorViewModel.CanRedo):
            case nameof(TerrainEditorViewModel.CanSave):
                SyncCommandState();
                break;
        }
    }

    private void SyncSelection()
    {
        _syncingSelection = true;
        TerrainList.SelectedItem = _viewModel.SelectedTerrain;
        _syncingSelection = false;
    }

    private void SyncFields()
    {
        NameBox.Text = _viewModel.Name;
        ColorBox.Text = _viewModel.ColorOverrideText;
    }

    private void SyncErrors()
    {
        NameErrorText.Text = _viewModel.NameError ?? string.Empty;
        NameErrorText.IsVisible = _viewModel.NameError is not null;
        ColorErrorText.Text = _viewModel.ColorError ?? string.Empty;
        ColorErrorText.IsVisible = _viewModel.ColorError is not null;
    }

    private void SyncValidation()
        => ValidationList.ItemsSource = _viewModel.Errors.Concat(_viewModel.Warnings).ToList();

    private void SyncCommandState()
    {
        SaveButton.IsEnabled = _viewModel.CanSave;
        RevertButton.IsEnabled = _viewModel.IsDirty;
        UndoButton.IsEnabled = _viewModel.CanUndo;
        RedoButton.IsEnabled = _viewModel.CanRedo;
    }

    private void SyncSheetDiagnostic()
    {
        string? diagnostic = _sheetControl.Diagnostic;
        SheetDiagnosticText.Text = diagnostic ?? string.Empty;
        SheetDiagnosticText.IsVisible = diagnostic is not null;
    }

    private void SyncRecoveryPanel()
    {
        bool malformed = !_controller.IsTerrainFeaturesEnabled;
        RecoveryPanel.IsVisible = malformed;
        if (malformed)
        {
            RecoveryText.Text = _controller.Context.Terrain.Diagnostic ?? "The terrain source is invalid.";
        }
    }

    private void OnNameTextChanged(object? sender, TextChangedEventArgs e)
        => _viewModel.Name = NameBox.Text ?? string.Empty;

    private void OnColorTextChanged(object? sender, TextChangedEventArgs e)
        => _viewModel.ColorOverrideText = ColorBox.Text ?? string.Empty;

    private void OnTerrainSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || TerrainList.SelectedItem is not TerrainEditorItemViewModel item)
        {
            return;
        }

        _viewModel.SelectedTerrain = item;
    }

    private void OnSheetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SheetCombo.SelectedItem is int sheet && sheet != _viewModel.SelectedSheet)
        {
            _viewModel.SelectedSheet = sheet;
        }
    }

    private void OnZoomSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ZoomCombo.SelectedItem is double zoom)
        {
            _viewModel.Zoom = zoom;
        }
    }

    private void OnResetZoom(object? sender, RoutedEventArgs e)
        => _viewModel.ResetZoom();

    private void OnUndo(object? sender, RoutedEventArgs e)
        => _viewModel.Undo();

    private void OnRedo(object? sender, RoutedEventArgs e)
        => _viewModel.Redo();

    private void OnRevert(object? sender, RoutedEventArgs e)
        => _viewModel.Revert();

    private void OnAdd(object? sender, RoutedEventArgs e)
        => _viewModel.AddTerrain();

    private void OnDelete(object? sender, RoutedEventArgs e)
        => _viewModel.DeleteSelectedTerrain();

    private void OnResetColor(object? sender, RoutedEventArgs e)
        => _viewModel.ResetColorOverride();

    private void OnSheetImageChanged(object? sender, EventArgs e)
        => SyncSheetDiagnostic();

    private void OnSheetInvalidated()
        => SyncSheetDiagnostic();

    private void OnControllerStateChanged()
    {
        if (!string.Equals(_sheetDirectory, _controller.Context.Cache.AssetDirectory, StringComparison.Ordinal))
        {
            ReplaceSheetControl();
        }

        BindViewModel(_controller.ViewModel);
        SyncRecoveryPanel();
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_controller.Gate.IsBusy)
        {
            return;
        }

        try
        {
            await _controller.SaveAsync();
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(new ErrorPresentation("Save terrain", ex.Message));
        }
    }

    private async void OnReplaceWithEmptyCatalog(object? sender, RoutedEventArgs e)
    {
        if (_controller.Gate.IsBusy)
        {
            return;
        }

        try
        {
            await _controller.ReplaceWithEmptyCatalogAsync();
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(new ErrorPresentation("Replace terrain", ex.Message));
        }
    }

    private async Task HandleClosingAsync(WindowClosingEventArgs e)
    {
        // The dirty prompt awaits a user choice, so OnClosing returns before the task
        // completes; without this the window closes while the dialog is still open.
        e.Cancel = true;
        try
        {
            while (true)
            {
                if (_closed)
                {
                    return;
                }

                if (_controller.Gate.IsBusy)
                {
                    e.Cancel = true;
                    await _controller.Gate.Completion;
                    continue;
                }

                TerrainEditorViewModel viewModel = _controller.ViewModel;
                if (viewModel.IsDirty)
                {
                    DirtyChoice choice = await _dialogs.ShowDirtyAsync("Terrain");
                    if (_closed)
                    {
                        return;
                    }

                    switch (choice)
                    {
                        case DirtyChoice.Save:
                            await _controller.SaveAsync();
                            if (_closed)
                            {
                                return;
                            }

                            if (_controller.ViewModel.IsDirty)
                            {
                                e.Cancel = true;
                                return;
                            }

                            break;
                        case DirtyChoice.Discard:
                            _controller.ViewModel.Revert();
                            break;
                        default:
                            e.Cancel = true;
                            return;
                    }
                }

                if (!viewModel.CommitPending())
                {
                    e.Cancel = true;
                    return;
                }

                if (!viewModel.IsDirty)
                {
                    break;
                }
            }

            if (_closed)
            {
                return;
            }

            e.Cancel = false;
            ApproveClose();
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            e.Cancel = true;
            await _dialogs.ShowErrorAsync(new ErrorPresentation("Close terrain", ex.Message));
        }
        finally
        {
            _closeGuardRunning = false;
        }
    }

    private void ApproveClose()
    {
        _closeApproved = true;
        _viewModel.CancelRegionStroke();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.SheetInvalidated -= OnSheetInvalidated;
        _sheetControl.Images.ImageChanged -= OnSheetImageChanged;
        _controller.StateChanged -= OnControllerStateChanged;
        SheetHost.Child = null;
        _sheetControl.Dispose();
        Close();
    }

    internal void ForceClose()
    {
        if (!_closed)
        {
            ApproveClose();
        }
    }
}
