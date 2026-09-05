using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MapEditor.App.Dialogs;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using Xunit;

namespace MapEditor.App.Tests;

public class TabStripTests
{
    private readonly MainWindowHarness _harness = MainWindowHarness.Create();

    public void Dispose() => _harness.Dispose();

    private ListBox Strip
        => _harness.Window.FindControl<ListBox>("TabStrip")
           ?? throw new System.InvalidOperationException("missing TabStrip");

    private ListBoxItem TabFor(MapDocumentViewModel document)
    {
        if (Strip.ContainerFromItem(document) is not ListBoxItem item)
        {
            throw new System.InvalidOperationException("no tab container for document");
        }

        return item;
    }

    private static string TabLabel(ListBoxItem tab)
        => tab.GetVisualDescendants().OfType<TextBlock>().First().Text;

    private static Avalonia.Controls.Shapes.Ellipse TabDirtyDot(ListBoxItem tab)
        => tab.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single();

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

        string path = WriteMap("open.bytes");
        _harness.Dialogs.OpenPickResult = path;
        await _harness.Workspace.OpenAsync();
        MapDocumentViewModel opened = _harness.Workspace.ActiveDocument;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("open.bytes", TabLabel(TabFor(opened)));
        Assert.Equal(Path.GetFullPath(path), opened.TabToolTip);
        Assert.Equal("Untitled", TabLabel(TabFor(initial)));
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

        _harness.Dialogs.SavePickResult = Path.Combine(_harness.TempDirectory, "saved.bytes");
        await document.SaveAsync();

        Assert.False(document.IsDirty);
        Assert.False(TabDirtyDot(tab).IsVisible);
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

        Assert.Same(second, _harness.Workspace.ActiveDocument);
        Assert.False(TabFor(first).IsSelected);
    }
}
