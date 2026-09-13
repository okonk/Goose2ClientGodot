using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MapEditor.App;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Rendering;
using MapEditor.App.Terrain;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

internal enum TerrainSheetPng
{
    Valid,
    Missing,
    Corrupt
}

internal sealed class TerrainEditorWindowHarness : IDisposable
{
    private const string ManifestJson = """
        { "tileSize": 32, "sheets": {
          "1": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32] },
          "2": { "20": [0, 0, 32, 32] } } }
        """;

    public string TempDirectory { get; }

    public FakeEditorDialogs Dialogs { get; } = new();

    public FakeTerrainCatalogPublisher Publisher { get; } = new();

    public TerrainWindowFileOperations Operations { get; }

    public TerrainCatalogFileStore Store { get; }

    public AssetContext Context { get; }

    public TerrainEditorController Controller { get; }

    public ISpriteSheetLoader Loader { get; }

    public TerrainEditorWindow Window { get; }

    public TerrainEditorViewModel ViewModel => Controller.ViewModel;

    public TerrainEditorSession Session => Controller.Session;

    public string SourcePath => Path.Combine(TempDirectory, TerrainAssetCatalog.FileName);

    private TerrainEditorWindowHarness(byte[]? terrainBytes, ISpriteSheetLoader windowLoader, bool show, TerrainSheetPng firstSheet)
    {
        TempDirectory = Directory.CreateTempSubdirectory("map-editor-terrain-window-").FullName;
        Operations = new();
        Store = new(Operations);
        File.WriteAllText(Path.Combine(TempDirectory, "manifest.json"), ManifestJson);
        Directory.CreateDirectory(Path.Combine(TempDirectory, "sheets"));
        switch (firstSheet)
        {
            case TerrainSheetPng.Valid:
                File.WriteAllBytes(Path.Combine(TempDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 32));
                break;
            case TerrainSheetPng.Corrupt:
                File.WriteAllText(Path.Combine(TempDirectory, "sheets", "1.png"), "this is not a png");
                break;
        }

        File.WriteAllBytes(Path.Combine(TempDirectory, "sheets", "2.png"), AssetFixture.PngSheet.Create(64, 32));
        if (terrainBytes is not null)
        {
            File.WriteAllBytes(SourcePath, terrainBytes);
        }

        Operations.AddDirectory(TempDirectory);
        if (terrainBytes is not null)
        {
            Operations.WriteFile(SourcePath, terrainBytes);
        }

        Context = AssetContext.Create(TempDirectory, new CountingSpriteSheetLoader());
        Controller = new TerrainEditorController(Context, Store, Publisher, Dialogs);
        Loader = windowLoader;
        Window = new TerrainEditorWindow(Controller, Dialogs, Context, Loader);
        if (show)
        {
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }
    }

    public static TerrainEditorWindowHarness Create(
        byte[]? terrainBytes,
        ISpriteSheetLoader? windowLoader = null,
        bool show = true,
        TerrainSheetPng firstSheet = TerrainSheetPng.Valid)
        => new(terrainBytes, windowLoader ?? new PngSpriteSheetLoader(), show, firstSheet);

    public void Dispose()
    {
        if (Window.IsVisible)
        {
            Dialogs.DirtyResult = DirtyChoice.Discard;
            Window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        Controller.Dispose();
        Context.Dispose();
        Directory.Delete(TempDirectory, recursive: true);
    }
}

internal sealed class PngSpriteSheetLoader : ISpriteSheetLoader
{
    public List<string> LoadedPaths { get; } = new();

    public SpriteSheetLoadResult Load(string path)
    {
        LoadedPaths.Add(path);
        return SpriteSheetLoadResult.Success(new AvaloniaSpriteSheetImage(new Bitmap(new MemoryStream(AssetFixture.PngSheet.Create(64, 32)))));
    }
}

internal sealed class TerrainWindowFileOperations : ITerrainCatalogFileOperations
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

    public void AddDirectory(string path)
        => _directories.Add(path);

    public void WriteFile(string path, byte[] bytes)
        => _files[path] = bytes;

    public byte[]? ReadFileDirect(string path)
        => _files.TryGetValue(path, out var bytes) ? bytes : null;

    public bool DirectoryExists(string path)
        => _directories.Contains(path);

    public bool FileExists(string path)
        => _files.ContainsKey(path);

    public byte[] ReadFile(string path)
        => _files[path];

    public void CommitFile(string path, byte[] bytes)
        => _files[path] = bytes;

    public Stream CreateNew(string path)
        => new RecordingStream(this, path);

    public void FlushToDisk(Stream stream)
    {
    }

    public void AtomicOverwrite(string source, string destination)
    {
        _files[destination] = _files[source];
        _files.Remove(source);
    }

    public void Delete(string path)
        => _files.Remove(path);

    private sealed class RecordingStream : Stream
    {
        private readonly TerrainWindowFileOperations _operations;
        private readonly string _path;
        private readonly MemoryStream _buffer = new();

        public RecordingStream(TerrainWindowFileOperations operations, string path)
        {
            _operations = operations;
            _path = path;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => _buffer.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _operations.CommitFile(_path, _buffer.ToArray());
            }

            base.Dispose(disposing);
        }
    }
}

public class TerrainEditorWindowTests
{
    private static readonly Guid GrassId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DirtId = new("22222222-2222-2222-2222-222222222222");

    private static T Find<T>(TerrainEditorWindowHarness harness, string name) where T : Control
        => harness.Window.FindControl<T>(name)
           ?? throw new InvalidOperationException($"missing named control {name}");

    private static byte[] Serialize(TerrainCatalog catalog)
        => Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(catalog));

    private static TerrainCatalog CreateCatalog(params (Guid Id, string Name)[] terrains)
    {
        var definitions = new List<TerrainDefinition>();
        var graphics = new List<TerrainGraphicDefinition>();
        int graphic = 10;
        foreach ((Guid id, string name) in terrains)
        {
            definitions.Add(new TerrainDefinition(id, name, null));
            graphics.Add(new TerrainGraphicDefinition(new TerrainGraphicReference(1, graphic), new TerrainPattern(Center: id)));
            graphic++;
        }

        return new TerrainCatalog(definitions, graphics);
    }

    private static byte[] DefaultCatalog()
        => Serialize(CreateCatalog((GrassId, "Grass"), (DirtId, "Dirt")));

    private static TerrainDefinition ById(TerrainEditorSession session, Guid id)
        => session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == id);

    // The headless platform exposes no title-bar API; CloseCore is the entry point the
    // platform uses for OS-initiated close requests (isProgrammatic: false).
    private static void TriggerClose(TerrainEditorWindow window, bool viaTitleBar)
    {
        if (viaTitleBar)
        {
            typeof(Window).GetMethod("CloseCore", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, [WindowCloseReason.WindowClosing, false, false]);
        }
        else
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InitialState_PopulatesTheListSheetAndZoomSelectors()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());
        var list = Find<ListBox>(harness, "TerrainList");

        Assert.Equal(2, list.Items.Count);
        var selected = Assert.IsType<TerrainEditorItemViewModel>(list.SelectedItem);
        Assert.Equal("Dirt", selected.Name);
        Assert.Same(harness.ViewModel.SelectedTerrain, selected);

        var sheets = ((IEnumerable)Find<ComboBox>(harness, "SheetCombo").ItemsSource!).Cast<int>().ToArray();
        Assert.Equal(new[] { 1, 2 }, sheets);
        Assert.Equal(1, Find<ComboBox>(harness, "SheetCombo").SelectedItem);
        Assert.Equal(1.0, Find<ComboBox>(harness, "ZoomCombo").SelectedItem);

        Assert.True(Find<Button>(harness, "SaveButton").IsEnabled);
        Assert.False(Find<Button>(harness, "RevertButton").IsEnabled);
        Assert.False(Find<Button>(harness, "UndoButton").IsEnabled);
        Assert.False(Find<Button>(harness, "RedoButton").IsEnabled);
    }

    [AvaloniaFact]
    public void AddButton_CreatesAndSelectsANewTerrain()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());

        Find<Button>(harness, "AddButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var added = ById(harness.Session, Assert.Single(harness.Session.CurrentCatalog.Terrains, terrain => terrain.Name == "Terrain").Id);
        Assert.Equal(3, harness.Session.CurrentCatalog.Terrains.Count);
        Assert.Equal("Terrain", added.Name);
        Assert.Equal("Terrain", harness.ViewModel.SelectedTerrain!.Name);
        Assert.Equal("Terrain", Find<TextBox>(harness, "NameBox").Text);
        Assert.Equal(3, Find<ListBox>(harness, "TerrainList").Items.Count);
        Assert.True(Find<Button>(harness, "UndoButton").IsEnabled);
    }

    [AvaloniaFact]
    public void NameAndColorFields_CommitWhenTheSelectionMoves()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());
        var list = Find<ListBox>(harness, "TerrainList");
        var first = (TerrainEditorItemViewModel)list.SelectedItem!;
        var other = (TerrainEditorItemViewModel)list.Items.Cast<TerrainEditorItemViewModel>().Single(item => item.Id != first.Id);

        Find<TextBox>(harness, "NameBox").Text = "Meadow";
        Find<TextBox>(harness, "ColorBox").Text = "#112233";
        Dispatcher.UIThread.RunJobs();
        list.SelectedItem = other;
        Dispatcher.UIThread.RunJobs();

        var renamed = ById(harness.Session, first.Id);
        Assert.Equal("Meadow", renamed.Name);
        Assert.Equal(new TerrainColor(0x11, 0x22, 0x33), renamed.ColorOverride);
        Assert.Equal(other.Name, Find<TextBox>(harness, "NameBox").Text);
        Assert.Equal(other.ColorOverrideText, Find<TextBox>(harness, "ColorBox").Text);
    }

    [AvaloniaFact]
    public void DeleteButton_RemovesTheSelectedTerrain()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());
        var list = Find<ListBox>(harness, "TerrainList");
        var first = (TerrainEditorItemViewModel)list.SelectedItem!;

        Find<Button>(harness, "DeleteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, harness.Session.CurrentCatalog.Terrains.Count);
        Assert.DoesNotContain(harness.Session.CurrentCatalog.Terrains, terrain => terrain.Id == first.Id);
        var remaining = Assert.Single(harness.ViewModel.Terrains);
        Assert.Same(remaining, harness.ViewModel.SelectedTerrain);
    }

    [AvaloniaFact]
    public void ResetColorButton_ClearsTheOverride()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());
        var list = Find<ListBox>(harness, "TerrainList");
        var first = (TerrainEditorItemViewModel)list.SelectedItem!;
        var other = (TerrainEditorItemViewModel)list.Items.Cast<TerrainEditorItemViewModel>().Single(item => item.Id != first.Id);

        Find<TextBox>(harness, "ColorBox").Text = "#AABBCC";
        Dispatcher.UIThread.RunJobs();
        list.SelectedItem = other;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new TerrainColor(0xAA, 0xBB, 0xCC), ById(harness.Session, first.Id).ColorOverride);

        list.SelectedItem = (TerrainEditorItemViewModel)harness.ViewModel.Terrains.Single(item => item.Id == first.Id);
        Dispatcher.UIThread.RunJobs();
        Find<Button>(harness, "ResetColorButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Null(ById(harness.Session, first.Id).ColorOverride);
        Assert.Equal(string.Empty, Find<TextBox>(harness, "ColorBox").Text);
    }

    [AvaloniaFact]
    public void SheetSelection_ChangesTheControlSheet()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());
        var loader = (PngSpriteSheetLoader)harness.Loader;

        Find<ComboBox>(harness, "SheetCombo").SelectedItem = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, harness.ViewModel.SelectedSheet);
        Assert.Equal(2, harness.Window.SheetControl.Images.SelectedSheet);
        Assert.EndsWith("sheets/2.png", loader.LoadedPaths[^1]);
    }

    [AvaloniaFact]
    public void ZoomSelectorAndReset_PropagateToTheViewModel()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());

        Find<ComboBox>(harness, "ZoomCombo").SelectedItem = 4.0;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4.0, harness.ViewModel.Zoom);

        Find<Button>(harness, "Percent100Button").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1.0, harness.ViewModel.Zoom);
        Assert.Equal(1.0, Find<ComboBox>(harness, "ZoomCombo").SelectedItem);
    }

    [AvaloniaFact]
    public void ZoomChange_RescalesTheScrollExtent()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());
        double extentAtOne = harness.Window.SheetScroll.Extent.Width;
        Assert.True(extentAtOne > 0);

        Find<ComboBox>(harness, "ZoomCombo").SelectedItem = 4.0;
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.SheetScroll.Extent.Width > extentAtOne);
    }

    [AvaloniaFact]
    public void InvalidNameAndColor_ShowTheValidationText()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());

        Find<TextBox>(harness, "NameBox").Text = "Grass";
        Find<TextBox>(harness, "ColorBox").Text = "red";
        Dispatcher.UIThread.RunJobs();
        Find<Button>(harness, "AddButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var nameError = Find<TextBlock>(harness, "NameErrorText");
        Assert.True(nameError.IsVisible);
        Assert.Equal("A terrain named 'Grass' already exists.", nameError.Text);
        var colorError = Find<TextBlock>(harness, "ColorErrorText");
        Assert.True(colorError.IsVisible);
        Assert.Equal("Color must be empty or #RRGGBB.", colorError.Text);
        Assert.Equal(2, harness.Session.CurrentCatalog.Terrains.Count);
    }

    [AvaloniaFact]
    public void SaveButton_SavesTheDirtyCatalogThroughTheController()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());
        harness.Session.RenameTerrain(GrassId, "Meadow");
        Dispatcher.UIThread.RunJobs();
        Assert.True(Find<Button>(harness, "SaveButton").IsEnabled);

        Find<Button>(harness, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, harness.Publisher.CommittedSaveCount);
        Assert.False(harness.ViewModel.IsDirty);
        var saved = TerrainCatalogJson.Parse(Encoding.UTF8.GetString(harness.Operations.ReadFileDirect(harness.SourcePath)!));
        Assert.Equal("Meadow", saved.Terrains.Single(terrain => terrain.Id == GrassId).Name);
    }

    [AvaloniaFact]
    public void RevertButton_DiscardsThePendingEdits()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());
        harness.Session.RenameTerrain(GrassId, "Meadow");
        Dispatcher.UIThread.RunJobs();
        Assert.True(harness.ViewModel.IsDirty);
        Assert.True(Find<Button>(harness, "RevertButton").IsEnabled);

        Find<Button>(harness, "RevertButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.ViewModel.IsDirty);
        Assert.Equal("Grass", ById(harness.Session, GrassId).Name);
        Assert.False(Find<Button>(harness, "RevertButton").IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void Close_Dirty_AlwaysResolvesSaveDiscardOrCancel(int choiceValue, bool viaTitleBar)
    {
        DirtyChoice choice = (DirtyChoice)choiceValue;
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());
        harness.Session.RenameTerrain(GrassId, "Meadow");
        Dispatcher.UIThread.RunJobs();
        harness.Dialogs.DirtyResult = choice;

        TriggerClose(harness.Window, viaTitleBar);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, harness.Dialogs.DirtyShown);
        switch (choice)
        {
            case DirtyChoice.Save:
                Assert.False(harness.Window.IsVisible);
                Assert.Equal(1, harness.Publisher.CommittedSaveCount);
                Assert.False(harness.ViewModel.IsDirty);
                break;
            case DirtyChoice.Discard:
                Assert.False(harness.Window.IsVisible);
                Assert.Equal(0, harness.Publisher.CommittedSaveCount);
                Assert.False(harness.ViewModel.IsDirty);
                Assert.Equal("Grass", ById(harness.Session, GrassId).Name);
                break;
            default:
                Assert.True(harness.Window.IsVisible);
                Assert.True(harness.ViewModel.IsDirty);
                Assert.Equal("Meadow", ById(harness.Session, GrassId).Name);
                break;
        }
    }

    [AvaloniaFact]
    public void Close_Clean_ClosesWithoutPrompting()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());
        var loader = (PngSpriteSheetLoader)harness.Loader;
        ISpriteSheetImage image = harness.Window.SheetControl.Images.Image!;

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(0, harness.Dialogs.DirtyShown);
        Assert.Null(Find<Border>(harness, "SheetHost").Child);
        Assert.Equal(1, ((AvaloniaSpriteSheetImage)image).DisposeCount);
        Assert.Equal(1, loader.LoadedPaths.Count);
    }

    [AvaloniaFact]
    public void Close_DetachesAndDisposesAllResources()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());
        var loader = (PngSpriteSheetLoader)harness.Loader;
        ISpriteSheetImage image = harness.Window.SheetControl.Images.Image!;
        int loadsBefore = loader.LoadedPaths.Count;

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Null(Find<Border>(harness, "SheetHost").Child);
        Assert.Equal(1, ((AvaloniaSpriteSheetImage)image).DisposeCount);

        harness.Session.RenameTerrain(GrassId, "Renamed");
        harness.ViewModel.Zoom = 4.0;
        harness.ViewModel.SelectedSheet = 2;
        harness.Dialogs.ConfirmReplaceMalformedResult = true;
        harness.Controller.ReplaceWithEmptyCatalogAsync().GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(loadsBefore, loader.LoadedPaths.Count);
        Assert.Equal("Dirt", Find<TextBox>(harness, "NameBox").Text);
        Assert.Equal(2, Find<ListBox>(harness, "TerrainList").Items.Count);
    }

    [AvaloniaFact]
    public async Task Close_WhileAnOperationIsBusy_WaitsForTheGateThenCloses()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());
        TerrainOperationLease lease = await harness.Controller.Gate.AcquireAsync();

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.Equal(0, harness.Dialogs.DirtyShown);
        Assert.NotNull(Find<Border>(harness, "SheetHost").Child);

        lease.Dispose();
        await WaitUntilAsync(() => !harness.Window.IsVisible);
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Null(Find<Border>(harness, "SheetHost").Child);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(5);
        }
    }

    [AvaloniaFact]
    public void Window_UndoRedo_ChangesOnlyDraftSession()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog());
        var map = new MapDocumentViewModel(
            new EditorDocumentController(
                new FakeEditorDialogs(),
                new MapFileStore(),
                new EditorDocument(new MapEditSession(MapDocument.Create(10, 10), initiallyDirty: false), null, null)),
            new SharedTileClipboard());
        MapEditSession mapSession = map.Session;
        mapSession.SelectedTileLayer = new MapTileLayer(1, 1);
        mapSession.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(mapSession.CompleteStroke());
        map.Refresh(EditorRefresh.Commands);
        Assert.True(map.CanUndo);
        MapTile painted = mapSession.Document[0, 0];
        Assert.False(painted.IsEmpty);

        harness.Session.RenameTerrain(GrassId, "Grass2");
        Dispatcher.UIThread.RunJobs();
        harness.Window.SheetControl.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal("Grass", ById(harness.Session, GrassId).Name);
        Assert.Equal(painted, mapSession.Document[0, 0]);
        Assert.True(map.CanUndo);

        harness.Window.KeyPressQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.None);
        Assert.Equal("Grass", ById(harness.Session, GrassId).Name);

        harness.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal("Grass2", ById(harness.Session, GrassId).Name);
        Assert.Equal(painted, mapSession.Document[0, 0]);
        Assert.True(map.CanUndo);

        harness.Window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control);
        Assert.Equal("Grass2", ById(harness.Session, GrassId).Name);
        Assert.Equal(painted, mapSession.Document[0, 0]);
        Assert.True(map.CanUndo);

        harness.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal("Grass", ById(harness.Session, GrassId).Name);
        Assert.Equal(painted, mapSession.Document[0, 0]);
        Assert.True(map.CanUndo);

        map.Dispose();
    }

    [AvaloniaFact]
    public void Show_WithOwner_LeavesTheOwnerEnabled()
    {
        using var harness = TerrainEditorWindowHarness.Create(DefaultCatalog(), show: false);
        var owner = new Window { Title = "Owner", Width = 400, Height = 300 };
        owner.Show();
        Dispatcher.UIThread.RunJobs();

        harness.Window.Show(owner);
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.True(owner.IsVisible);
        Assert.True(owner.IsEnabled);

        owner.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void MissingSheetImage_ShowsTheLoaderDiagnostic()
    {
        using var harness = TerrainEditorWindowHarness.Create(
            DefaultCatalog(),
            windowLoader: new AvaloniaSpriteSheetLoader(),
            firstSheet: TerrainSheetPng.Missing);

        var diagnostic = Find<TextBlock>(harness, "SheetDiagnosticText");
        Assert.True(diagnostic.IsVisible);
        Assert.Contains("not found", diagnostic.Text);
        Assert.Null(harness.Window.SheetControl.Images.Image);
    }

    [AvaloniaFact]
    public void CorruptSheetImage_ShowsTheLoaderDiagnostic()
    {
        using var harness = TerrainEditorWindowHarness.Create(
            DefaultCatalog(),
            windowLoader: new AvaloniaSpriteSheetLoader(),
            firstSheet: TerrainSheetPng.Corrupt);

        var diagnostic = Find<TextBlock>(harness, "SheetDiagnosticText");
        Assert.True(diagnostic.IsVisible);
        Assert.Contains("not a decodable PNG", diagnostic.Text);
        Assert.Null(harness.Window.SheetControl.Images.Image);
    }

    [AvaloniaFact]
    public void MalformedSource_ShowsTheRecoveryPanelAndReplaceRebindsTheWindow()
    {
        byte[] malformed = Encoding.UTF8.GetBytes("this is not a terrain catalog");
        using var harness = TerrainEditorWindowHarness.Create(malformed);
        Assert.False(harness.Controller.IsTerrainFeaturesEnabled);

        var panel = Find<Border>(harness, "RecoveryPanel");
        Assert.True(panel.IsVisible);
        Assert.Contains(harness.Context.Terrain.Diagnostic!, Find<TextBlock>(harness, "RecoveryText").Text);

        harness.Dialogs.ConfirmReplaceMalformedResult = true;
        Find<Button>(harness, "ReplaceCatalogButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Controller.IsTerrainFeaturesEnabled);
        Assert.Empty(harness.ViewModel.Terrains);
        Assert.Equal(0, Find<ListBox>(harness, "TerrainList").Items.Count);
        Assert.Equal(string.Empty, Find<TextBox>(harness, "NameBox").Text);
        Assert.True(panel.IsVisible);
        Assert.Equal(1, harness.Dialogs.ConfirmReplaceMalformedShown);
    }

    [AvaloniaFact]
    public void MalformedSource_DeclinedReplaceKeepsTheCurrentEditor()
    {
        byte[] malformed = Encoding.UTF8.GetBytes("this is not a terrain catalog");
        using var harness = TerrainEditorWindowHarness.Create(malformed);
        TerrainEditorViewModel before = harness.ViewModel;
        harness.Dialogs.ConfirmReplaceMalformedResult = false;

        Find<Button>(harness, "ReplaceCatalogButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Same(before, harness.ViewModel);
        Assert.Equal(1, harness.Dialogs.ConfirmReplaceMalformedShown);
        Assert.True(Find<Border>(harness, "RecoveryPanel").IsVisible);
    }

}