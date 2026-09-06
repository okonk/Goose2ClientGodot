using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MapEditor.App.Dialogs;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using Xunit;
using Path = System.IO.Path;

namespace MapEditor.App.Tests;

public class TabStripTests : IDisposable
{
    private readonly MainWindowHarness _harness = MainWindowHarness.Create();

    public void Dispose() => _harness.Dispose();

    private ListBox Strip
        => _harness.Window.FindControl<ListBox>("TabStrip")
           ?? throw new InvalidOperationException("missing TabStrip");

    private ListBoxItem TabFor(MapDocumentViewModel document)
    {
        if (Strip.ContainerFromItem(document) is not ListBoxItem item)
        {
            throw new InvalidOperationException("no tab container for document");
        }

        return item;
    }

    private static string TabLabel(ListBoxItem tab)
        => tab.GetVisualDescendants().OfType<TextBlock>().First().Text;

    private static object? TabTip(ListBoxItem tab)
        => tab.GetVisualDescendants().OfType<StackPanel>().First(panel => panel.Classes.Contains("tabHeader"))
            .GetValue(ToolTip.TipProperty);

    private static StackPanel TabHeader(ListBoxItem tab)
        => tab.GetVisualDescendants().OfType<StackPanel>().First(panel => panel.Classes.Contains("tabHeader"));

    private Point TabPoint(Visual target, Point local)
        => target.TranslatePoint(local, _harness.Window).Value;

    private static Point PastMidpoint(Visual target)
        => new(target.Bounds.Width / 2 + 5, target.Bounds.Height / 2);

    private static Ellipse TabDirtyDot(ListBoxItem tab)
        => tab.GetVisualDescendants().OfType<Ellipse>().Single();

    private string WriteMap(string name)
    {
        string path = Path.Combine(_harness.TempDirectory, name);
        new MapFileStore().Save(path, MapDocument.Create(20, 15));
        return path;
    }

    private async Task<MapDocumentViewModel> NewDocumentAsync()
    {
        _harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await _harness.Workspace.NewAsync();
        Dispatcher.UIThread.RunJobs();
        return _harness.Workspace.ActiveDocument;
    }

    private void Edit(MapDocumentViewModel document)
    {
        document.Brush = new MapTileLayer(1, 2);
        document.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(document.Session.CompleteStroke());
        document.Refresh(EditorRefresh.Commands);
    }

    [AvaloniaFact]
    public async Task TabStrip_ShowsOneHeaderPerDocument()
    {
        Assert.Same(_harness.Workspace.Documents, Strip.ItemsSource);
        Assert.Single(Strip.Items);

        await NewDocumentAsync();
        await NewDocumentAsync();

        Assert.Equal(3, Strip.Items.Count);
        foreach (MapDocumentViewModel document in _harness.Workspace.Documents)
        {
            Assert.NotNull(TabFor(document));
        }
    }

    [AvaloniaFact]
    public async Task TabStrip_HeaderShowsFileName()
    {
        MapDocumentViewModel initial = _harness.ViewModel;
        Assert.Equal("Untitled", TabLabel(TabFor(initial)));
        Assert.Equal("Untitled", initial.TabToolTip);
        Assert.Equal("Untitled", TabTip(TabFor(initial)));

        string path = WriteMap("open.bytes");
        _harness.Dialogs.OpenPickResult = path;
        await _harness.Workspace.OpenAsync();
        MapDocumentViewModel opened = _harness.Workspace.ActiveDocument;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("open.bytes", TabLabel(TabFor(opened)));
        Assert.Equal(Path.GetFullPath(path), opened.TabToolTip);
        Assert.Equal(Path.GetFullPath(path), TabTip(TabFor(opened)));
        Assert.Null(Strip.ContainerFromItem(initial));
    }

    [AvaloniaFact]
    public async Task TabStrip_DirtyDot_AppearsOnEditAndClearsOnSave()
    {
        MapDocumentViewModel document = _harness.ViewModel;
        ListBoxItem tab = TabFor(document);

        Assert.False(document.IsDirty);
        Assert.False(TabDirtyDot(tab).IsVisible);

        Edit(document);

        Assert.True(document.IsDirty);
        Assert.True(TabDirtyDot(tab).IsVisible);

        string savedPath = Path.Combine(_harness.TempDirectory, "saved.bytes");
        _harness.Dialogs.SavePickResult = savedPath;
        await document.SaveAsync();

        Assert.False(document.IsDirty);
        Assert.False(TabDirtyDot(tab).IsVisible);
        Assert.Equal("saved.bytes", TabLabel(tab));
        Assert.Equal(Path.GetFullPath(savedPath), TabTip(tab));
    }

    [AvaloniaFact]
    public async Task TabStrip_InactiveTabDirtyDot_Updates()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();

        Edit(first);

        Assert.True(TabDirtyDot(TabFor(first)).IsVisible);
        Assert.False(TabDirtyDot(TabFor(second)).IsVisible);
    }

    [AvaloniaFact]
    public async Task TabStrip_ActiveHeader_IsSelected()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        ListBoxItem firstTab = TabFor(first);

        Assert.Same(first, Strip.SelectedItem);
        Assert.True(firstTab.IsSelected);

        MapDocumentViewModel second = await NewDocumentAsync();
        ListBoxItem secondTab = TabFor(second);

        Assert.Same(second, Strip.SelectedItem);
        Assert.True(secondTab.IsSelected);
        Assert.False(firstTab.IsSelected);
    }

    [AvaloniaFact]
    public async Task TabStrip_ActivatingOffscreenTab_ScrollsItIntoView()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        for (int i = 0; i < 15; i++)
        {
            await NewDocumentAsync();
        }

        Dispatcher.UIThread.RunJobs();
        ScrollViewer scroll = Strip.GetVisualDescendants().OfType<ScrollViewer>().First();
        Vector offsetAtLast = scroll.Offset;
        Assert.True(offsetAtLast.X > 0);

        _harness.Workspace.Activate(first);
        Dispatcher.UIThread.RunJobs();

        Assert.True(scroll.Offset.X < offsetAtLast.X);
    }

    [AvaloniaFact]
    public async Task TabStrip_SelectionChanged_DoesNotRecurse()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();

        int activeChanges = 0;
        _harness.Workspace.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.ActiveDocument))
            {
                activeChanges++;
            }
        };

        Strip.SelectedItem = first;
        Assert.Equal(1, activeChanges);
        Assert.Same(first, _harness.Workspace.ActiveDocument);

        Strip.SelectedItem = second;
        Assert.Equal(2, activeChanges);
        Assert.Same(second, _harness.Workspace.ActiveDocument);

        Strip.SelectedItem = first;
        Assert.Equal(3, activeChanges);
        Assert.Same(first, _harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task Tab_LeftClick_ActivatesDocument()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();

        TextBlock label = TabFor(second).GetVisualDescendants().OfType<TextBlock>().First();
        Point point = label.TranslatePoint(new Point(5, 5), _harness.Window).Value;
        _harness.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        _harness.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);

        Assert.Same(second, _harness.Workspace.ActiveDocument);
        Assert.True(TabFor(second).IsSelected);
        Assert.False(TabFor(first).IsSelected);
    }

    [AvaloniaFact]
    public async Task Tab_CloseButton_ClosesDocument()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();

        Button close = TabFor(first).GetVisualDescendants().OfType<Button>().First();
        Point point = close.TranslatePoint(new Point(8, 8), _harness.Window).Value;
        _harness.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        _harness.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(first, _harness.Workspace.Documents);
        Assert.Same(second, _harness.Workspace.ActiveDocument);
        Assert.Single(_harness.Workspace.Documents);
    }

    [AvaloniaFact]
    public async Task Tab_MiddleClick_ClosesDocument()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();

        TextBlock label = TabFor(first).GetVisualDescendants().OfType<TextBlock>().First();
        Point point = label.TranslatePoint(new Point(5, 5), _harness.Window).Value;
        _harness.Window.MouseDown(point, MouseButton.Middle, RawInputModifiers.None);
        _harness.Window.MouseUp(point, MouseButton.Middle, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(first, _harness.Workspace.Documents);
        Assert.Same(second, _harness.Workspace.ActiveDocument);
        Assert.Single(_harness.Workspace.Documents);
    }

    [AvaloniaFact]
    public async Task Tab_MiddlePressOnCloseButton_ClosesDocument()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();

        Button close = TabFor(second).GetVisualDescendants().OfType<Button>().First();
        Point point = close.TranslatePoint(new Point(8, 8), _harness.Window).Value;
        _harness.Window.MouseDown(point, MouseButton.Middle, RawInputModifiers.None);
        _harness.Window.MouseUp(point, MouseButton.Middle, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(second, _harness.Workspace.Documents);
        Assert.Same(first, _harness.Workspace.ActiveDocument);
        Assert.Single(_harness.Workspace.Documents);
    }

    [AvaloniaFact]
    public async Task Tab_CloseDirty_PromptsAndCancelKeepsTab()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();
        Edit(first);
        Dispatcher.UIThread.RunJobs();

        _harness.Dialogs.DirtyResult = DirtyChoice.Cancel;

        Button close = TabFor(first).GetVisualDescendants().OfType<Button>().First();
        Point point = close.TranslatePoint(new Point(8, 8), _harness.Window).Value;
        _harness.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        _harness.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, _harness.Dialogs.DirtyShown);
        Assert.Equal(2, _harness.Workspace.Documents.Count);
        Assert.Same(first, _harness.Workspace.ActiveDocument);
        Assert.NotNull(TabFor(first));
        Assert.NotNull(TabFor(second));
    }

    [AvaloniaFact]
    public async Task Tab_CloseLastTab_LeavesFreshUntitled()
    {
        MapDocumentViewModel only = _harness.ViewModel;

        Button close = TabFor(only).GetVisualDescendants().OfType<Button>().First();
        Point point = close.TranslatePoint(new Point(8, 8), _harness.Window).Value;
        _harness.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        _harness.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(_harness.Workspace.Documents);
        MapDocumentViewModel fresh = _harness.Workspace.ActiveDocument;
        Assert.NotSame(only, fresh);
        Assert.Equal("Untitled", fresh.TabTitle);
        Assert.NotNull(TabFor(fresh));
    }

    [AvaloniaFact]
    public async Task Tab_Click_CancelsArmedPasteMode()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();
        _harness.Workspace.Activate(first);
        Dispatcher.UIThread.RunJobs();

        first.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        first.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        first.CopySelection();
        first.BeginPasteMode();
        Assert.True(first.PasteMode);

        TextBlock label = TabFor(second).GetVisualDescendants().OfType<TextBlock>().First();
        Point point = label.TranslatePoint(new Point(5, 5), _harness.Window).Value;
        _harness.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        _harness.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);

        Assert.False(first.PasteMode);
        Assert.Same(second, _harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task Tab_CloseButtonOnInactiveTab_DoesNotActivateIt()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();
        MapDocumentViewModel third = await NewDocumentAsync();
        _harness.Workspace.Activate(first);
        Dispatcher.UIThread.RunJobs();

        int activeChanges = 0;
        _harness.Workspace.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.ActiveDocument))
            {
                activeChanges++;
            }
        };

        Button close = TabFor(third).GetVisualDescendants().OfType<Button>().First();
        Point point = close.TranslatePoint(new Point(8, 8), _harness.Window).Value;
        _harness.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        _harness.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(third, _harness.Workspace.Documents);
        Assert.Equal(0, activeChanges);
        Assert.Same(first, _harness.Workspace.ActiveDocument);
        Assert.True(TabFor(first).IsSelected);
        Assert.False(TabFor(second).IsSelected);
    }

    [AvaloniaFact]
    public async Task Drag_PastNeighbourMidpoint_ReordersTabs()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();

        StackPanel firstHeader = TabHeader(TabFor(first));
        StackPanel secondHeader = TabHeader(TabFor(second));
        Point press = TabPoint(firstHeader, new Point(5, 5));
        Point past = TabPoint(secondHeader, PastMidpoint(secondHeader));
        _harness.Window.MouseDown(press, MouseButton.Left, RawInputModifiers.None);
        _harness.Window.MouseMove(past, RawInputModifiers.None);

        Assert.Equal(new[] { second, first }, _harness.Workspace.Documents);
        Assert.Same(first, _harness.Workspace.ActiveDocument);

        _harness.Window.MouseUp(past, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new[] { second, first }, _harness.Workspace.Documents);
    }

    [AvaloniaFact]
    public async Task Drag_MultiStep_ReordersAcrossSeveralTabs()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();
        MapDocumentViewModel third = await NewDocumentAsync();

        StackPanel firstHeader = TabHeader(TabFor(first));
        Point press = TabPoint(firstHeader, new Point(5, 5));
        _harness.Window.MouseDown(press, MouseButton.Left, RawInputModifiers.None);

        StackPanel secondHeader = TabHeader(TabFor(second));
        _harness.Window.MouseMove(TabPoint(secondHeader, PastMidpoint(secondHeader)), RawInputModifiers.None);
        Assert.Equal(new[] { second, first, third }, _harness.Workspace.Documents);
        Dispatcher.UIThread.RunJobs();

        StackPanel thirdHeader = TabHeader(TabFor(third));
        _harness.Window.MouseMove(TabPoint(thirdHeader, PastMidpoint(thirdHeader)), RawInputModifiers.None);
        Assert.Equal(new[] { second, third, first }, _harness.Workspace.Documents);

        _harness.Window.MouseUp(TabPoint(thirdHeader, PastMidpoint(thirdHeader)), MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new[] { second, third, first }, _harness.Workspace.Documents);
    }

    [AvaloniaFact]
    public async Task Drag_DocumentRemovedMidDrag_EndsDragCleanly()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();
        MapDocumentViewModel third = await NewDocumentAsync();

        StackPanel firstHeader = TabHeader(TabFor(first));
        Point press = TabPoint(firstHeader, new Point(5, 5));
        _harness.Window.MouseDown(press, MouseButton.Left, RawInputModifiers.None);

        await _harness.Workspace.CloseAsync(first);
        Dispatcher.UIThread.RunJobs();

        StackPanel thirdHeader = TabHeader(TabFor(third));
        _harness.Window.MouseMove(TabPoint(thirdHeader, PastMidpoint(thirdHeader)), RawInputModifiers.None);
        Assert.Equal(new[] { second, third }, _harness.Workspace.Documents);

        _harness.Window.MouseUp(TabPoint(thirdHeader, PastMidpoint(thirdHeader)), MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new[] { second, third }, _harness.Workspace.Documents);
    }

    [AvaloniaFact]
    public async Task Drag_OfBackgroundTab_ActivatesItOnPress()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();

        StackPanel secondHeader = TabHeader(TabFor(second));
        Point press = TabPoint(secondHeader, new Point(5, 5));
        _harness.Window.MouseDown(press, MouseButton.Left, RawInputModifiers.None);

        Assert.Same(second, _harness.Workspace.ActiveDocument);
        Assert.True(TabFor(second).IsSelected);

        _harness.Window.MouseUp(press, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new[] { first, second }, _harness.Workspace.Documents);
        Assert.Same(second, _harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task Drag_EscapeCancels_RestoresOriginalOrder()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();

        StackPanel firstHeader = TabHeader(TabFor(first));
        StackPanel secondHeader = TabHeader(TabFor(second));
        Point press = TabPoint(firstHeader, new Point(5, 5));
        Point past = TabPoint(secondHeader, PastMidpoint(secondHeader));
        _harness.Window.MouseDown(press, MouseButton.Left, RawInputModifiers.None);
        _harness.Window.MouseMove(past, RawInputModifiers.None);
        Assert.Equal(new[] { second, first }, _harness.Workspace.Documents);

        _harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal(new[] { first, second }, _harness.Workspace.Documents);
        Assert.Same(first, _harness.Workspace.ActiveDocument);

        _harness.Window.MouseUp(past, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new[] { first, second }, _harness.Workspace.Documents);
    }

    [AvaloniaFact]
    public async Task Drag_ShortPress_StillActivatesTab()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();

        StackPanel secondHeader = TabHeader(TabFor(second));
        Point press = TabPoint(secondHeader, new Point(5, 5));
        _harness.Window.MouseDown(press, MouseButton.Left, RawInputModifiers.None);
        _harness.Window.MouseUp(press, MouseButton.Left, RawInputModifiers.None);

        Assert.Same(second, _harness.Workspace.ActiveDocument);
        Assert.True(TabFor(second).IsSelected);
        Assert.False(TabFor(first).IsSelected);
        Assert.Equal(new[] { first, second }, _harness.Workspace.Documents);
    }

    [AvaloniaFact]
    public async Task Drag_CaptureLost_EndsDragCleanly()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();

        StackPanel firstHeader = TabHeader(TabFor(first));
        StackPanel secondHeader = TabHeader(TabFor(second));
        Point press = TabPoint(firstHeader, new Point(5, 5));
        Point past = TabPoint(secondHeader, PastMidpoint(secondHeader));
        IPointer? pointer = null;
        _harness.Window.AddHandler(InputElement.PointerMovedEvent, (s, e) => pointer = e.Pointer);
        _harness.Window.MouseDown(press, MouseButton.Left, RawInputModifiers.None);
        _harness.Window.MouseMove(past, RawInputModifiers.None);
        Assert.Equal(new[] { second, first }, _harness.Workspace.Documents);
        Assert.NotNull(pointer!.Captured);
        pointer.Capture(null);

        Point stray = TabPoint(secondHeader, new Point(0, secondHeader.Bounds.Height / 2));
        _harness.Window.MouseMove(stray, RawInputModifiers.None);
        Assert.Equal(new[] { second, first }, _harness.Workspace.Documents);

        _harness.Window.MouseUp(stray, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new[] { second, first }, _harness.Workspace.Documents);
    }

    [AvaloniaFact]
    public async Task Drag_PressOnCloseButton_DoesNotStartDragAndStillCloses()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();
        MapDocumentViewModel third = await NewDocumentAsync();
        _harness.Workspace.Activate(first);
        Dispatcher.UIThread.RunJobs();

        Button close = TabFor(first).GetVisualDescendants().OfType<Button>().First();
        Point press = close.TranslatePoint(new Point(8, 8), _harness.Window).Value;
        StackPanel secondHeader = TabHeader(TabFor(second));
        Point past = TabPoint(secondHeader, PastMidpoint(secondHeader));
        _harness.Window.MouseDown(press, MouseButton.Left, RawInputModifiers.None);
        _harness.Window.MouseMove(past, RawInputModifiers.None);
        _harness.Window.MouseUp(press, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(first, _harness.Workspace.Documents);
        Assert.Equal(new[] { second, third }, _harness.Workspace.Documents);
        Assert.Same(second, _harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task TabStrip_PressingBackgroundTab_ActivatesIt()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();

        TextBlock label = TabFor(first).GetVisualDescendants().OfType<TextBlock>().First();
        Point point = label.TranslatePoint(new Point(5, 5), _harness.Window).Value;
        _harness.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        _harness.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);

        Assert.Same(first, _harness.Workspace.ActiveDocument);
        Assert.True(TabFor(first).IsSelected);
        Assert.False(TabFor(second).IsSelected);
    }

    [AvaloniaFact]
    public async Task TabStrip_PressingCloseButton_DoesNotSelectTheTab()
    {
        MapDocumentViewModel first = _harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync();

        Button close = TabFor(first).GetVisualDescendants().OfType<Button>().First();
        Point point = close.TranslatePoint(new Point(8, 8), _harness.Window).Value;
        _harness.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        _harness.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(first, _harness.Workspace.Documents);
        Assert.Same(second, _harness.Workspace.ActiveDocument);
        Assert.True(TabFor(second).IsSelected);
    }
}
