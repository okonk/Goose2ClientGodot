using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MapEditor.App;
using MapEditor.App.Dialogs;
using MapEditor.App.Terrain;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class TerrainEditorEndToEndTests
{
    private static readonly MapTileLayer Empty = new(0, 0);

    private static (MainWindowHarness Harness, string AssetDirectory) CreateHarness(string name, string? terrainJson = null)
    {
        var harness = MainWindowHarness.Create();
        string assetDirectory = AssetFixture.WriteTerrainAssetDirectory(harness.TempDirectory, name, terrainJson);
        Assert.True(harness.Assets.TryOpen(assetDirectory));
        Dispatcher.UIThread.RunJobs();
        return (harness, assetDirectory);
    }

    private static T Find<T>(Window window, string name) where T : Control
        => window.FindControl<T>(name)
           ?? throw new InvalidOperationException($"missing named region {name}");

    private static TerrainEditorWindow OpenEditor(MainWindowHarness harness)
    {
        Find<Button>(harness.Window, "TerrainAddButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<TerrainEditorWindow>(harness.Window.TerrainEditor);
    }

    private static TerrainEditorWindow OpenEditorForEdit(MainWindowHarness harness, string terrainName)
    {
        SelectComboTerrain(harness, terrainName);
        Find<Button>(harness.Window, "TerrainEditButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<TerrainEditorWindow>(harness.Window.TerrainEditor);
    }

    private static void SetName(TerrainEditorWindow editor, string name)
    {
        Find<TextBox>(editor, "NameBox").Text = name;
        Dispatcher.UIThread.RunJobs();
    }

    private static void CommitRename(TerrainEditorWindow editor, Guid id, string newName)
    {
        SetName(editor, newName);
        ListBox list = Find<ListBox>(editor, "TerrainList");
        var items = ((System.Collections.IEnumerable)list.Items).Cast<TerrainEditorItemViewModel>().ToList();
        list.SelectedItem = items.Single(item => item.Id != id);
        Dispatcher.UIThread.RunJobs();
    }

    private static void ClickRegion(TerrainEditorWindow editor, int graphic, TerrainPeer peer)
    {
        (int X, int Y) local = peer switch
        {
            TerrainPeer.Center => (16, 16),
            TerrainPeer.East => (28, 16),
            TerrainPeer.North => (16, 4),
            TerrainPeer.South => (16, 28),
            TerrainPeer.West => (4, 16),
            TerrainPeer.NorthEast => (28, 5),
            TerrainPeer.SouthEast => (28, 27),
            TerrainPeer.SouthWest => (4, 27),
            TerrainPeer.NorthWest => (4, 5),
            _ => throw new ArgumentOutOfRangeException(nameof(peer))
        };
        Point windowPoint = editor.SheetControl.TranslatePoint(
            new Point((FrameX(graphic) + local.X) * editor.ViewModel.Zoom, local.Y * editor.ViewModel.Zoom), editor)!.Value;
        editor.MouseDown(windowPoint, MouseButton.Left, RawInputModifiers.None);
        editor.MouseUp(windowPoint, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static readonly Lazy<Dictionary<int, int>> _frameX = new(() =>
    {
        var map = new Dictionary<int, int>();
        using JsonDocument doc = JsonDocument.Parse(AssetFixture.TerrainManifestJson);
        JsonElement sheet = doc.RootElement.GetProperty("sheets").GetProperty("1");
        foreach (JsonProperty property in sheet.EnumerateObject())
        {
            map[int.Parse(property.Name)] = property.Value[0].GetInt32();
        }
        return map;
    });

    private static int FrameX(int graphic) => _frameX.Value[graphic];

    private static void ClickTile(MainWindowHarness harness, int x, int y, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Point windowPoint = harness.Window.Canvas.TranslatePoint(
            new Point(x * 32 + 16, y * 32 + 16), harness.Window)!.Value;
        harness.Window.MouseDown(windowPoint, MouseButton.Left, modifiers);
        harness.Window.MouseUp(windowPoint, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static MapTileLayer Tile(MapDocument document, int x, int y)
        => document[x, y].GetLayer(0);

    private static void SelectComboTerrain(MainWindowHarness harness, string name)
    {
        ComboBox combo = Find<ComboBox>(harness.Window, "TerrainCombo");
        int index = ((System.Collections.IEnumerable)combo.Items).Cast<TerrainChoice>()
            .ToList().FindIndex(choice => choice.Name == name);
        Assert.True(index >= 0, $"terrain {name} not in selector");
        combo.SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
        ToggleButton tool = Find<ToggleButton>(harness.Window, "TerrainTool");
        if (tool.IsChecked != true)
        {
            tool.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static string TerrainPath(string assetDirectory)
        => Path.Combine(assetDirectory, TerrainAssetCatalog.FileName);

    [AvaloniaFact]
    public async Task FullWorkflow_AuthorSavePublishPaintEraseUndoRedoSaveReopen()
    {
        var (harness, assetDirectory) = CreateHarness("assets-e2e");
        MapDocumentViewModel document = harness.ViewModel;

        TerrainCatalogLoadResult availability = document.TerrainAvailability!;
        Assert.True(availability.IsValid);
        Assert.False(availability.CanPaint);
        Assert.True(Find<Button>(harness.Window, "TerrainAddButton").IsEnabled);
        Assert.False(Find<ComboBox>(harness.Window, "TerrainCombo").IsEnabled);

        TerrainEditorWindow editor = OpenEditor(harness);
        TerrainEditorController controller = harness.Window.TerrainEditorController!;
        TerrainEditorSession session = controller.Session;
        Assert.True(editor.IsVisible);
        Assert.Same(harness.Assets.Gate, controller.Gate);
        Assert.Equal("Terrain", session.CurrentCatalog.Terrains.Single().Name);
        Guid grassId = editor.ViewModel.SelectedTerrain!.Id;

        ClickRegion(editor, 10, TerrainPeer.Center);
        ClickRegion(editor, 11, TerrainPeer.Center);
        ClickRegion(editor, 11, TerrainPeer.East);
        ClickRegion(editor, 12, TerrainPeer.Center);
        ClickRegion(editor, 12, TerrainPeer.NorthEast);
        ClickRegion(editor, 13, TerrainPeer.Center);
        ClickRegion(editor, 13, TerrainPeer.North);

        SetName(editor, "Grass");
        Find<Button>(editor, "AddButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Guid waterId = editor.ViewModel.SelectedTerrain!.Id;
        ClickRegion(editor, 20, TerrainPeer.Center);
        ClickRegion(editor, 21, TerrainPeer.Center);
        ClickRegion(editor, 21, TerrainPeer.East);

        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        ClickRegion(editor, 21, TerrainPeer.East);
        Assert.Equal(waterId, session.CurrentCatalog.Graphics.Single(g => g.Reference.Graphic == 21).Pattern.East);
        Find<Button>(editor, "UndoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(session.CurrentCatalog.Graphics.Single(g => g.Reference.Graphic == 21).Pattern.East);
        Assert.True(session.CanRedo);
        Find<Button>(editor, "RedoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(waterId, session.CurrentCatalog.Graphics.Single(g => g.Reference.Graphic == 21).Pattern.East);

        SetName(editor, "Water");
        Find<Button>(editor, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(harness.Dialogs.Errors);
        Assert.False(controller.ViewModel.IsDirty);

        byte[] savedBytes = File.ReadAllBytes(TerrainPath(assetDirectory));
        Assert.Equal(Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(session.CurrentCatalog)), savedBytes);
        Assert.Equal(TerrainFileRevision.FromBytes(savedBytes), controller.Revision);
        Assert.Equal(session.CurrentCatalog, harness.Assets.Current.Terrain.Catalog);

        ComboBox combo = Find<ComboBox>(harness.Window, "TerrainCombo");
        Assert.True(combo.IsEnabled);
        Assert.Equal(new[] { "Grass", "Water" }, ((System.Collections.IEnumerable)combo.Items).Cast<TerrainChoice>().Select(choice => choice.Name).ToArray());
        Assert.Equal(new[] { grassId, waterId }, document.Terrains.Select(choice => choice.Id).ToArray());

        SelectComboTerrain(harness, "Grass");
        Assert.Equal(grassId, document.SelectedTerrainId);
        Assert.Equal(MapEditTool.Terrain, document.ActiveTool);
        Assert.True(Find<ToggleButton>(harness.Window, "TerrainTool").IsChecked);

        MapEditSession mapSession = document.Session;
        MapDocument map = mapSession.Document;
        long history = mapSession.HistoryVersion;
        ClickTile(harness, 0, 0);
        Assert.Equal(history + 1, mapSession.HistoryVersion);
        ClickTile(harness, 1, 0);
        Assert.Equal(history + 2, mapSession.HistoryVersion);
        ClickTile(harness, 0, 1);
        Assert.Equal(history + 3, mapSession.HistoryVersion);
        ClickTile(harness, 0, 2);
        Assert.Equal(history + 4, mapSession.HistoryVersion);
        AssertTileState(map, afterBlock: true);

        ClickTile(harness, 0, 1, RawInputModifiers.Shift);
        Assert.Equal(history + 5, mapSession.HistoryVersion);
        AssertTileState(map, afterBlock: false);

        Assert.True(document.Undo());
        Assert.Equal(history + 6, mapSession.HistoryVersion);
        AssertTileState(map, afterBlock: true);
        Assert.True(document.Redo());
        Assert.Equal(history + 7, mapSession.HistoryVersion);
        AssertTileState(map, afterBlock: false);

        string mapPath = Path.Combine(harness.TempDirectory, "e2e.map");
        harness.Dialogs.SavePickResult = mapPath;
        await document.SaveAsync();
        Assert.Empty(harness.Dialogs.Errors);
        Assert.Equal(mapPath, document.Document.Path);
        Assert.False(document.IsDirty);

        MapDocument decoded = new MapFileStore().Open(mapPath).Document;
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                Assert.Equal(Tile(map, x, y), Tile(decoded, x, y));
            }
        }

        Assert.True(await harness.Workspace.CloseAsync(document));
        harness.Dialogs.OpenPickResult = mapPath;
        await harness.Workspace.OpenAsync();
        MapDocumentViewModel reopened = harness.Workspace.ActiveDocument;
        Assert.NotSame(document, reopened);
        Assert.Equal(mapPath, reopened.Document.Path);
        Assert.False(reopened.IsDirty);
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                Assert.Equal(Tile(decoded, x, y), Tile(reopened.Session.Document, x, y));
            }
        }

        harness.Dispose();
    }

    private static void AssertTileState(MapDocument map, bool afterBlock)
    {
        if (afterBlock)
        {
            Assert.Equal(new MapTileLayer(1, 11), Tile(map, 0, 0));
            Assert.Equal(new MapTileLayer(1, 10), Tile(map, 1, 0));
            Assert.Equal(new MapTileLayer(1, 13), Tile(map, 0, 1));
            Assert.Equal(new MapTileLayer(1, 13), Tile(map, 0, 2));
        }
        else
        {
            Assert.Equal(new MapTileLayer(1, 11), Tile(map, 0, 0));
            Assert.Equal(new MapTileLayer(1, 10), Tile(map, 1, 0));
            Assert.Equal(Empty, Tile(map, 0, 1));
            Assert.Equal(new MapTileLayer(1, 10), Tile(map, 0, 2));
        }
    }

    [AvaloniaFact]
    public async Task PublicationDuringActiveTerrainStroke_CancelsTheStrokeAndPublishesTheCatalog()
    {
        var (harness, assetDirectory) = CreateHarness("assets-stroke", AssetFixture.GrassWaterCatalogJson);
        MapDocumentViewModel document = harness.ViewModel;
        SelectComboTerrain(harness, "Grass");

        TerrainEditorWindow editor = OpenEditorForEdit(harness, "Grass");
        TerrainEditorController controller = harness.Window.TerrainEditorController!;
        SetName(editor, "Meadow");

        Point windowPoint = harness.Window.Canvas.TranslatePoint(new Point(1 * 32 + 16, 1 * 32 + 16), harness.Window)!.Value;
        harness.Window.MouseDown(windowPoint, MouseButton.Left, RawInputModifiers.None);
        Assert.True(document.Session.HasActiveStroke);
        Assert.True(harness.Canvas.IsGestureActive);
        Assert.NotEqual(Empty, Tile(document.Session.Document, 1, 1));

        Find<Button>(editor, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(harness.Dialogs.Errors);

        Assert.False(document.Session.HasActiveStroke);
        Assert.False(harness.Canvas.IsGestureActive);
        Assert.Equal(Empty, Tile(document.Session.Document, 1, 1));
        Assert.False(document.Session.CanUndo);
        Assert.False(controller.ViewModel.IsDirty);

        TerrainCatalog saved = TerrainCatalogJson.Parse(File.ReadAllText(TerrainPath(assetDirectory)));
        Assert.Equal("Meadow", saved.Terrains.Single(terrain => terrain.Id == AssetFixture.GrassId).Name);
        Assert.Equal(new[] { "Meadow", "Water" }, document.Terrains.Select(choice => choice.Name).ToArray());
        Assert.Equal(AssetFixture.GrassId, document.SelectedTerrainId);
        Assert.Equal(MapEditTool.Terrain, document.ActiveTool);

        harness.Dispose();
    }

    [AvaloniaFact]
    public async Task Save_ExternalTerrainChange_ReloadOverwriteAndCancel()
    {
        var external = new TerrainCatalog(
            new[] { new TerrainDefinition(AssetFixture.WaterId, "Water", null) },
            new[]
            {
                new TerrainGraphicDefinition(new TerrainGraphicReference(1, 20), new TerrainPattern(Center: AssetFixture.WaterId))
            });
        byte[] externalBytes = Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(external));

        var (harness, assetDirectory) = CreateHarness("assets-conflict-reload", AssetFixture.GrassWaterCatalogJson);
        TerrainEditorWindow editor = OpenEditorForEdit(harness, "Grass");
        TerrainEditorController controller = harness.Window.TerrainEditorController!;
        SetName(editor, "Meadow");
        File.WriteAllBytes(TerrainPath(assetDirectory), externalBytes);
        harness.Dialogs.ReplaceTerrainCatalogResult = TerrainExternalChangeChoice.Reload;
        Find<Button>(editor, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(harness.Dialogs.Errors);
        Assert.Equal(1, harness.Dialogs.ReplaceTerrainCatalogShown);
        Assert.Equal(TerrainPath(assetDirectory), harness.Dialogs.LastReplaceTerrainCatalogPath);
        Assert.Equal(TerrainCatalogJson.Parse(Encoding.UTF8.GetString(externalBytes)), controller.Session.CurrentCatalog);
        Assert.Equal(TerrainFileRevision.FromBytes(externalBytes), controller.Revision);
        Assert.False(controller.ViewModel.IsDirty);
        Assert.Equal(externalBytes, File.ReadAllBytes(TerrainPath(assetDirectory)));
        Assert.Equal(new[] { "Water" }, harness.ViewModel.Terrains.Select(choice => choice.Name).ToArray());
        harness.Dispose();

        var (second, secondDirectory) = CreateHarness("assets-conflict-overwrite", AssetFixture.GrassWaterCatalogJson);
        TerrainEditorWindow secondEditor = OpenEditorForEdit(second, "Grass");
        TerrainEditorController secondController = second.Window.TerrainEditorController!;
        SetName(secondEditor, "Meadow");
        File.WriteAllBytes(TerrainPath(secondDirectory), externalBytes);
        second.Dialogs.ReplaceTerrainCatalogResult = TerrainExternalChangeChoice.Overwrite;
        Find<Button>(secondEditor, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(second.Dialogs.Errors);
        Assert.Equal(1, second.Dialogs.ReplaceTerrainCatalogShown);
        Assert.False(secondController.ViewModel.IsDirty);
        var draft = TerrainCatalogJson.Parse(AssetFixture.GrassWaterCatalogJson);
        byte[] expectedDraft = Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(new TerrainCatalog(
            draft.Terrains.Select(terrain => terrain.Id == AssetFixture.GrassId ? terrain with { Name = "Meadow" } : terrain),
            draft.Graphics)));
        Assert.Equal(expectedDraft, File.ReadAllBytes(TerrainPath(secondDirectory)));
        Assert.Equal("Meadow", secondController.Session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == AssetFixture.GrassId).Name);
        second.Dispose();

        var (third, thirdDirectory) = CreateHarness("assets-conflict-cancel", AssetFixture.GrassWaterCatalogJson);
        TerrainEditorWindow thirdEditor = OpenEditorForEdit(third, "Grass");
        TerrainEditorController thirdController = third.Window.TerrainEditorController!;
        SetName(thirdEditor, "Meadow");
        File.WriteAllBytes(TerrainPath(thirdDirectory), externalBytes);
        third.Dialogs.ReplaceTerrainCatalogResult = TerrainExternalChangeChoice.Cancel;
        Find<Button>(thirdEditor, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(third.Dialogs.Errors);
        Assert.Equal(1, third.Dialogs.ReplaceTerrainCatalogShown);
        Assert.True(thirdController.ViewModel.IsDirty);
        Assert.Equal(externalBytes, File.ReadAllBytes(TerrainPath(thirdDirectory)));
        Assert.Equal(new[] { "Grass", "Water" }, third.ViewModel.Terrains.Select(choice => choice.Name).ToArray());
        third.Dispose();
    }

    [AvaloniaFact]
    public async Task MalformedTerrainCatalog_RecoveryKeepsTilesUsable()
    {
        var (harness, assetDirectory) = CreateHarness("assets-malformed", AssetFixture.MalformedTerrainJson);
        MapDocumentViewModel document = harness.ViewModel;

        TerrainCatalogLoadResult availability = document.TerrainAvailability!;
        Assert.False(availability.IsValid);
        Assert.True(availability.CanAuthor);
        Assert.NotNull(availability.Diagnostic);
        string diagnosticMessage = availability.Diagnostic!;
        TextBlock diagnostic = Find<TextBlock>(harness.Window, "TerrainDiagnosticText");
        Assert.True(diagnostic.IsVisible);
        Assert.Equal(diagnosticMessage, diagnostic.Text);
        Assert.False(Find<ComboBox>(harness.Window, "TerrainCombo").IsEnabled);
        Assert.True(Find<Button>(harness.Window, "TerrainAddButton").IsEnabled);

        document.Brush = new MapTileLayer(1, 10);
        ClickTile(harness, 0, 0);
        Assert.Equal(new MapTileLayer(1, 10), Tile(document.Session.Document, 0, 0));

        TerrainEditorWindow editor = OpenEditor(harness);
        TerrainEditorController controller = harness.Window.TerrainEditorController!;
        Assert.False(controller.IsTerrainFeaturesEnabled);
        Assert.True(Find<Border>(editor, "RecoveryPanel").IsVisible);
        Assert.Contains(diagnosticMessage, Find<TextBlock>(editor, "RecoveryText").Text);

        harness.Dialogs.ConfirmReplaceMalformedResult = true;
        Find<Button>(editor, "ReplaceCatalogButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(harness.Dialogs.Errors);
        Assert.Equal(1, harness.Dialogs.ConfirmReplaceMalformedShown);
        Assert.False(controller.IsTerrainFeaturesEnabled);
        Assert.Empty(controller.Session.CurrentCatalog.Terrains);
        Assert.True(Find<Border>(editor, "RecoveryPanel").IsVisible);

        Assert.False(Find<ComboBox>(harness.Window, "TerrainCombo").IsEnabled);
        Assert.True(Find<Button>(harness.Window, "TerrainAddButton").IsEnabled);
        document.Brush = new MapTileLayer(2, 20);
        ClickTile(harness, 0, 1);
        Assert.Equal(new MapTileLayer(2, 20), Tile(document.Session.Document, 0, 1));

        harness.Dispose();
    }

    [AvaloniaFact]
    public void OverlappingFramesCatalog_IsRejectedAndTilesStayUsable()
    {
        var (harness, _) = CreateHarness("assets-overlap", AssetFixture.OverlappingFramesCatalogJson);
        MapDocumentViewModel document = harness.ViewModel;

        TerrainCatalogLoadResult availability = document.TerrainAvailability!;
        Assert.False(availability.IsValid);
        Assert.Contains(availability.Issues, issue =>
            issue.Code == TerrainValidationCode.DuplicateGraphicReference
            && issue.GraphicReference == new TerrainGraphicReference(1, 10));
        Assert.False(Find<ComboBox>(harness.Window, "TerrainCombo").IsEnabled);
        Assert.True(Find<Button>(harness.Window, "TerrainAddButton").IsEnabled);

        document.Brush = new MapTileLayer(1, 10);
        ClickTile(harness, 0, 0);
        Assert.Equal(new MapTileLayer(1, 10), Tile(document.Session.Document, 0, 0));
        Assert.True(document.Undo());
        Assert.Equal(Empty, Tile(document.Session.Document, 0, 0));

        harness.Dispose();
    }

    [AvaloniaFact]
    public async Task DirtyTerrainEditor_RootSwitch_SavesDiscardsOrCancels()
    {
        foreach (DirtyChoice choice in new[] { DirtyChoice.Save, DirtyChoice.Discard, DirtyChoice.Cancel })
        {
            var (harness, assetA) = CreateHarness("assets-root-a", AssetFixture.GrassWaterCatalogJson);
            string assetB = AssetFixture.WriteTerrainAssetDirectory(harness.TempDirectory, "assets-root-b", null);
            TerrainEditorWindow editor = OpenEditorForEdit(harness, "Grass");
            TerrainEditorController controller = harness.Window.TerrainEditorController!;
            TerrainCatalog loaded = controller.Session.CurrentCatalog;
            CommitRename(editor, AssetFixture.GrassId, "Meadow");
            Assert.True(controller.ViewModel.IsDirty);
            TerrainCatalog draft = controller.Session.CurrentCatalog;
            byte[] originalA = File.ReadAllBytes(TerrainPath(assetA));

            harness.Dialogs.DirtyResult = choice;
            harness.Dialogs.AssetDirectoryPickResult = assetB;
            Find<Button>(harness.Window, "LoadAssetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(harness.Dialogs.Errors);
            Assert.Equal(1, harness.Dialogs.DirtyShown);
            Assert.Equal("Terrain", harness.Dialogs.LastDirtyDisplayName);

            switch (choice)
            {
                case DirtyChoice.Save:
                {
                    TerrainCatalog savedA = TerrainCatalogJson.Parse(File.ReadAllText(TerrainPath(assetA)));
                    Assert.Equal("Meadow", savedA.Terrains.Single(terrain => terrain.Id == AssetFixture.GrassId).Name);
                    Assert.Same(harness.Window.TerrainEditorController, controller);
                    Assert.True(editor.IsVisible);
                    Assert.Same(draft, controller.Session.CurrentCatalog);
                    Assert.Equal(TerrainFileRevision.Missing, controller.Revision);
                    Assert.True(controller.IsTerrainFeaturesEnabled);
                    Assert.False(controller.ViewModel.IsDirty);
                    Assert.Empty(harness.ViewModel.Terrains);
                    Assert.False(Find<ComboBox>(harness.Window, "TerrainCombo").IsEnabled);
                    Assert.Equal(Path.GetFullPath(assetB), harness.Assets.Current.Cache.AssetDirectory);
                    break;
                }
                case DirtyChoice.Discard:
                {
                    Assert.Equal(originalA, File.ReadAllBytes(TerrainPath(assetA)));
                    Assert.Same(harness.Window.TerrainEditorController, controller);
                    Assert.True(editor.IsVisible);
                    Assert.Equal(loaded, controller.Session.CurrentCatalog);
                    Assert.Equal(TerrainFileRevision.Missing, controller.Revision);
                    Assert.True(controller.IsTerrainFeaturesEnabled);
                    Assert.False(controller.ViewModel.IsDirty);
                    Assert.Empty(harness.ViewModel.Terrains);
                    break;
                }
                default:
                {
                    Assert.Equal(originalA, File.ReadAllBytes(TerrainPath(assetA)));
                    Assert.Same(harness.Window.TerrainEditorController, controller);
                    Assert.True(editor.IsVisible);
                    Assert.True(controller.ViewModel.IsDirty);
                    Assert.Same(draft, controller.Session.CurrentCatalog);
                    Assert.Equal(new[] { "Grass", "Water" }, harness.ViewModel.Terrains.Select(item => item.Name).ToArray());
                    Assert.Equal(Path.GetFullPath(assetA), harness.Assets.Current.Cache.AssetDirectory);
                    break;
                }
            }

            harness.Dialogs.DirtyResult = DirtyChoice.Discard;
            harness.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task RemovedTerrain_LeavesPaintedMapTilesUnchanged()
    {
        var (harness, assetDirectory) = CreateHarness("assets-removed", AssetFixture.GrassWaterCatalogJson);
        MapDocumentViewModel document = harness.ViewModel;
        SelectComboTerrain(harness, "Grass");
        ClickTile(harness, 1, 1);
        Assert.Equal(new MapTileLayer(1, 10), Tile(document.Session.Document, 1, 1));

        Find<Button>(harness.Window, "TerrainEditButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        TerrainEditorWindow editor = Assert.IsType<TerrainEditorWindow>(harness.Window.TerrainEditor);
        TerrainEditorController controller = harness.Window.TerrainEditorController!;
        Assert.Equal(AssetFixture.GrassId, editor.ViewModel.SelectedTerrain!.Id);

        Find<Button>(editor, "DeleteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(controller.Session.CurrentCatalog.Terrains, terrain => terrain.Id == AssetFixture.GrassId);

        Find<Button>(editor, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(harness.Dialogs.Errors);
        TerrainCatalog saved = TerrainCatalogJson.Parse(File.ReadAllText(TerrainPath(assetDirectory)));
        Assert.DoesNotContain(saved.Terrains, terrain => terrain.Id == AssetFixture.GrassId);
        Assert.DoesNotContain(saved.Graphics, graphic => graphic.Reference.Graphic is 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18);

        Assert.Equal(new MapTileLayer(1, 10), Tile(document.Session.Document, 1, 1));
        Assert.Null(document.SelectedTerrainId);
        Assert.Equal(MapEditTool.Pencil, document.ActiveTool);
        Assert.False(Find<ToggleButton>(harness.Window, "TerrainTool").IsChecked);
        Assert.Equal(new[] { "Water" }, document.Terrains.Select(item => item.Name).ToArray());

        document.Brush = new MapTileLayer(1, 10);
        Find<ToggleButton>(harness.Window, "EraserTool").IsChecked = true;
        ClickTile(harness, 1, 1);
        Assert.Equal(Empty, Tile(document.Session.Document, 1, 1));

        string mapPath = Path.Combine(harness.TempDirectory, "removed.map");
        harness.Dialogs.SavePickResult = mapPath;
        await document.SaveAsync();
        Assert.Empty(harness.Dialogs.Errors);
        MapDocument decoded = new MapFileStore().Open(mapPath).Document;
        Assert.Equal(Empty, Tile(decoded, 1, 1));

        harness.Dispose();
    }

    [AvaloniaFact]
    public void CorruptSheet_TerrainEditorShowsDiagnosticAndTilesStayUsable()
    {
        var (harness, assetDirectory) = CreateHarness("assets-corrupt-sheet", AssetFixture.GrassWaterCatalogJson);
        AssetFixture.WriteCorruptSheet(assetDirectory, 1);
        MapDocumentViewModel document = harness.ViewModel;

        TerrainEditorWindow editor = OpenEditor(harness);
        TextBlock sheetDiagnostic = Find<TextBlock>(editor, "SheetDiagnosticText");
        Assert.True(sheetDiagnostic.IsVisible);
        Assert.Contains("not a decodable PNG", sheetDiagnostic.Text);
        Assert.Null(editor.SheetControl.Images.Image);

        document.Brush = new MapTileLayer(2, 20);
        ClickTile(harness, 0, 0);
        Assert.Equal(new MapTileLayer(2, 20), Tile(document.Session.Document, 0, 0));

        harness.Dispose();
    }
}
