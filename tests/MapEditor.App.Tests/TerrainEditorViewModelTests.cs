using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.App.Terrain;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class TerrainEditorViewModelTests
{
    private static readonly Guid GrassId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DirtId = new("00000000-0000-0000-0000-000000000002");
    private static readonly TerrainGraphicReference GrassGraphic = new(1, 10);
    private static readonly TerrainGraphicReference DirtGraphic = new(1, 20);

    private static SpriteManifest CreateManifest()
        => SpriteManifest.Parse(
            "{ \"tileSize\": 32, \"sheets\": { \"1\": { \"10\": [0, 0, 32, 32], \"20\": [32, 0, 32, 32] }, \"2\": { \"40\": [0, 0, 32, 32] } } }");

    private static TerrainCatalog CreateBaseline()
        => new(
            new List<TerrainDefinition>
            {
                new(GrassId, "Grass", null),
                new(DirtId, "Dirt", null)
            },
            new List<TerrainGraphicDefinition>
            {
                new(GrassGraphic, new TerrainPattern(Center: GrassId, North: DirtId)),
                new(DirtGraphic, new TerrainPattern(Center: DirtId, South: GrassId))
            });

    private static (TerrainEditorViewModel ViewModel, TerrainEditorSession Session, TerrainCatalog Baseline) CreateViewModel()
    {
        var baseline = CreateBaseline();
        var session = new TerrainEditorSession(baseline, CreateManifest());
        return (new TerrainEditorViewModel(session, CreateManifest()), session, baseline);
    }

    private static TerrainEditorItemViewModel Item(TerrainEditorViewModel vm, Guid id)
        => vm.Terrains.Single(item => item.Id == id);

    [Fact]
    public void Terrains_AreOrderedByNameThenId()
    {
        var idA = new Guid("00000000-0000-0000-0000-000000000002");
        var idB = new Guid("00000000-0000-0000-0000-000000000001");
        var idC = new Guid("00000000-0000-0000-0000-000000000003");
        var catalog = new TerrainCatalog(
            new List<TerrainDefinition>
            {
                new(idB, "B", null),
                new(idA, "A", null),
                new(idC, "b", null)
            },
            new List<TerrainGraphicDefinition>
            {
                new(new TerrainGraphicReference(1, 1), new TerrainPattern(Center: idB)),
                new(new TerrainGraphicReference(1, 2), new TerrainPattern(Center: idA)),
                new(new TerrainGraphicReference(1, 3), new TerrainPattern(Center: idC))
            });
        var manifest = SpriteManifest.Parse(
            "{ \"tileSize\": 32, \"sheets\": { \"1\": { \"1\": [0, 0, 32, 32], \"2\": [32, 0, 32, 32], \"3\": [64, 0, 32, 32] } } }");
        var vm = new TerrainEditorViewModel(new TerrainEditorSession(catalog, manifest), manifest);

        Assert.Equal(new[] { "A", "B", "b" }, vm.Terrains.Select(item => item.Name));
        Assert.Equal(idA, vm.Terrains[0].Id);
        Assert.Equal(idB, vm.Terrains[1].Id);
        Assert.Equal(idC, vm.Terrains[2].Id);
    }

    [Fact]
    public void SelectedTerrain_DefaultsToFirst_AndSelectionNeverMutatesCatalog()
    {
        var (vm, _, baseline) = CreateViewModel();

        Assert.Equal(DirtId, vm.SelectedTerrain!.Id);
        var catalog = vm.CurrentCatalog;

        vm.SelectedTerrain = Item(vm, GrassId);

        Assert.Equal(GrassId, vm.SelectedTerrain!.Id);
        Assert.Same(catalog, vm.CurrentCatalog);
        Assert.Equal(baseline, vm.CurrentCatalog);
    }

    [Fact]
    public void EligibleSheets_RequireExactTileSizeFrames()
    {
        var manifest = SpriteManifest.Parse(
            "{ \"tileSize\": 32, \"sheets\": { " +
            "\"1\": { \"10\": [0, 0, 32, 32], \"20\": [32, 0, 32, 32], \"30\": [64, 0, 16, 16] }, " +
            "\"2\": { \"40\": [0, 0, 16, 16] }, " +
            "\"3\": { \"50\": [0, 0, 32, 32] } } }");
        var baseline = CreateBaseline();
        var vm = new TerrainEditorViewModel(new TerrainEditorSession(baseline, manifest), manifest);

        Assert.Equal(new[] { 1, 3 }, vm.EligibleSheets);
        Assert.Equal(1, vm.SelectedSheet);
        Assert.Equal(new[] { 10, 20 }, vm.EligibleFrames.Select(frame => frame.Reference.Graphic));

        vm.SelectedSheet = 3;
        Assert.Equal(new[] { 50 }, vm.EligibleFrames.Select(frame => frame.Reference.Graphic));

        vm.SelectedSheet = null;
        Assert.Empty(vm.EligibleFrames);
    }

    [Fact]
    public void Zoom_ClampsToViewerRange()
    {
        var (vm, _, _) = CreateViewModel();

        vm.Zoom = 0.01;
        Assert.Equal(0.25, vm.Zoom);
        vm.Zoom = 100;
        Assert.Equal(8.0, vm.Zoom);
        vm.Zoom = 2.5;
        Assert.Equal(2.5, vm.Zoom);
        vm.ResetZoom();
        Assert.Equal(1.0, vm.Zoom);
    }

    [Fact]
    public void Swatches_UseDerivedColorAndOverride()
    {
        var (vm, session, _) = CreateViewModel();

        Assert.Equal(TerrainColor.Derive(GrassId), Item(vm, GrassId).Swatch);
        Assert.False(Item(vm, GrassId).HasColorOverride);
        Assert.Equal(string.Empty, Item(vm, GrassId).ColorOverrideText);

        vm.SelectedTerrain = Item(vm, GrassId);
        vm.ColorOverrideText = "#ff0000";
        Assert.True(vm.CommitPending());

        var item = Item(vm, GrassId);
        Assert.Equal(new TerrainColor(255, 0, 0), item.Swatch);
        Assert.True(item.HasColorOverride);
        Assert.Equal("#FF0000", item.ColorOverrideText);
        Assert.Equal(
            new TerrainColor(255, 0, 0),
            session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == GrassId).ColorOverride);
    }

    [Fact]
    public void CommitPending_ValidNameAndColor_UpdatesSession()
    {
        var (vm, session, _) = CreateViewModel();
        vm.SelectedTerrain = Item(vm, DirtId);
        vm.Name = "Dirt2";
        vm.ColorOverrideText = "#00ff00";

        Assert.True(vm.CommitPending());
        Assert.Null(vm.NameError);
        Assert.Null(vm.ColorError);
        Assert.Equal("Dirt2", session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == DirtId).Name);
        Assert.Equal(
            new TerrainColor(0, 255, 0),
            session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == DirtId).ColorOverride);
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanUndo);
        Assert.Equal("Dirt2", vm.Name);
    }

    [Fact]
    public void CommitPending_InvalidNameOrColor_PreservesSession()
    {
        var (vm, _, baseline) = CreateViewModel();
        var catalog = vm.CurrentCatalog;

        vm.SelectedTerrain = Item(vm, GrassId);
        vm.Name = "Dirt";
        vm.ColorOverrideText = "#12345";
        Assert.False(vm.CommitPending());
        Assert.NotNull(vm.NameError);
        Assert.NotNull(vm.ColorError);
        Assert.Same(catalog, vm.CurrentCatalog);
        Assert.Equal(baseline, vm.CurrentCatalog);
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
        Assert.False(vm.IsDirty);

        vm.Name = "Fresh";
        vm.ColorOverrideText = "nope";
        Assert.False(vm.CommitPending());
        Assert.Equal("Grass", Item(vm, GrassId).Name);
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);

        vm.Name = "  ";
        Assert.False(vm.CommitPending());
        Assert.NotNull(vm.NameError);
    }

    [Fact]
    public void RenameCollision_DoesNotMutateSessionOrClearRedo()
    {
        var (vm, session, _) = CreateViewModel();
        var added = vm.AddTerrain();
        Assert.NotNull(added);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        var catalog = vm.CurrentCatalog;

        vm.SelectedTerrain = Item(vm, added!.Value);
        vm.Name = "grass";
        Assert.False(vm.CommitPending());
        Assert.NotNull(vm.NameError);
        Assert.Same(catalog, vm.CurrentCatalog);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal("Terrain", Item(vm, added.Value).Name);

        vm.Name = "Fresh";
        Assert.True(vm.CommitPending());
        Assert.Null(vm.NameError);
        Assert.Equal("Fresh", Item(vm, added.Value).Name);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void MalformedColor_DoesNotMutateSessionOrClearRedo()
    {
        var (vm, session, _) = CreateViewModel();
        vm.AddTerrain();
        var catalog = vm.CurrentCatalog;

        foreach (var text in new[] { "FF0000", "#12345", "#1234567", "#GGGGGG", "#12345 " })
        {
            vm.ColorOverrideText = text;
            Assert.False(vm.CommitPending(), text);
            Assert.NotNull(vm.ColorError);
            Assert.Same(catalog, vm.CurrentCatalog);
            Assert.True(session.CanUndo);
            Assert.False(session.CanRedo);
        }

        vm.ColorOverrideText = "#abcdef";
        Assert.True(vm.CommitPending());
        Assert.Equal(
            new TerrainColor(0xAB, 0xCD, 0xEF),
            session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == vm.SelectedTerrain!.Id).ColorOverride);
    }

    [Fact]
    public void CanSave_CenterlessOrMissingFrame_IsFalse()
    {
        var (vm, session, _) = CreateViewModel();
        Assert.True(vm.CanSave);

        session.BeginRegionStroke(null);
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.Center));
        Assert.True(session.CompleteRegionStroke());

        Assert.False(vm.CanSave);
        var issue = vm.Errors.Single(error => error.Code == TerrainValidationCode.CenterlessGraphic);
        Assert.Equal(GrassGraphic, issue.GraphicReference);
        Assert.Equal(TerrainPeer.Center, issue.Peer);
        Assert.Contains("1, 10", issue.Message);
        Assert.Contains(vm.CurrentCatalog.Graphics, graphic => graphic.Reference == GrassGraphic);
        Assert.NotEmpty(vm.Warnings);

        Assert.True(session.Undo());
        Assert.True(vm.CanSave);
        Assert.Empty(vm.Errors);
    }

    [Fact]
    public void CanSave_MissingSpriteFrame_IsFalse()
    {
        var manifest = CreateManifest();
        var catalog = new TerrainCatalog(
            new List<TerrainDefinition>
            {
                new(GrassId, "Grass", null)
            },
            new List<TerrainGraphicDefinition>
            {
                new(new TerrainGraphicReference(9, 99), new TerrainPattern(Center: GrassId))
            });
        var vm = new TerrainEditorViewModel(new TerrainEditorSession(catalog, manifest), manifest);

        Assert.False(vm.CanSave);
        Assert.Contains(vm.Errors, error => error.Code == TerrainValidationCode.MissingSpriteFrame);
    }

    [Fact]
    public void ResetColor_UsesStableDerivedSwatch()
    {
        var (vm, session, _) = CreateViewModel();
        var derived = TerrainColor.Derive(GrassId);

        vm.SelectedTerrain = Item(vm, GrassId);
        vm.ColorOverrideText = "#FF0000";
        Assert.True(vm.CommitPending());
        Assert.Equal(new TerrainColor(255, 0, 0), Item(vm, GrassId).Swatch);

        vm.Name = "Renamed";
        Assert.True(vm.CommitPending());
        Assert.Equal(new TerrainColor(255, 0, 0), Item(vm, GrassId).Swatch);

        vm.ResetColorOverride();
        var item = Item(vm, GrassId);
        Assert.Equal(derived, item.Swatch);
        Assert.False(item.HasColorOverride);
        Assert.Equal(string.Empty, item.ColorOverrideText);
        Assert.Null(session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == GrassId).ColorOverride);
    }

    [Fact]
    public void SelectionChange_CommitsPendingTextFirst()
    {
        var (vm, session, _) = CreateViewModel();
        vm.SelectedTerrain = Item(vm, DirtId);
        vm.Name = "Dirt2";

        vm.SelectedTerrain = Item(vm, GrassId);

        Assert.Equal(GrassId, vm.SelectedTerrain!.Id);
        Assert.Equal("Dirt2", session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == DirtId).Name);
        Assert.Equal("Grass", vm.Name);
        Assert.Null(vm.NameError);
    }

    [Fact]
    public void SelectionChange_WithInvalidPendingText_IsBlocked()
    {
        var (vm, session, _) = CreateViewModel();
        vm.SelectedTerrain = Item(vm, DirtId);
        vm.Name = "Grass";

        vm.SelectedTerrain = Item(vm, GrassId);

        Assert.Equal(DirtId, vm.SelectedTerrain!.Id);
        Assert.NotNull(vm.NameError);
        Assert.Equal("Dirt", session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == DirtId).Name);
    }

    [Fact]
    public void UndoRedoRevert_CommitPendingFirst_AndTrackCommandFlags()
    {
        var (vm, _, baseline) = CreateViewModel();
        vm.SelectedTerrain = Item(vm, DirtId);
        vm.Name = "Dirt2";

        Assert.True(vm.Undo());
        Assert.Equal(baseline, vm.CurrentCatalog);
        Assert.Equal("Dirt", vm.Name);
        Assert.False(vm.CanUndo);
        Assert.True(vm.CanRedo);

        Assert.True(vm.Redo());
        Assert.Equal("Dirt2", Item(vm, DirtId).Name);
        Assert.True(vm.CanUndo);
        Assert.False(vm.CanRedo);

        vm.AddTerrain();
        vm.Revert();
        Assert.Equal(baseline, vm.CurrentCatalog);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void Undo_WithInvalidPendingText_IsBlocked()
    {
        var (vm, session, _) = CreateViewModel();
        session.AddTerrain();
        var catalog = vm.CurrentCatalog;

        vm.SelectedTerrain = Item(vm, DirtId);
        vm.Name = "  ";
        Assert.False(vm.Undo());
        Assert.Same(catalog, vm.CurrentCatalog);
        Assert.True(vm.CanUndo);
        Assert.NotNull(vm.NameError);
    }

    [Fact]
    public void PrepareMarkSaved_CommitsPending_AndBlocksWhenInvalid()
    {
        var (vm, session, _) = CreateViewModel();
        vm.SelectedTerrain = Item(vm, DirtId);
        vm.Name = "Dirt2";

        var prepared = vm.PrepareMarkSaved();
        Assert.NotNull(prepared);
        session.ApplyMarkSaved(prepared!);
        session.NotifyMarkSaved(prepared!);
        Assert.Equal("Dirt2", Item(vm, DirtId).Name);
        Assert.False(vm.IsDirty);

        vm.SelectedTerrain = Item(vm, GrassId);
        vm.Name = "Dirt2";
        Assert.Null(vm.PrepareMarkSaved());
        Assert.Equal("Grass", Item(vm, GrassId).Name);
    }

    [Fact]
    public void Preview_InvalidatesCanvasWithoutHistoryFlags()
    {
        var (vm, session, _) = CreateViewModel();
        var catalog = vm.CurrentCatalog;
        var canvas = 0;
        var sheet = 0;
        vm.CanvasInvalidated += () => canvas++;
        vm.SheetInvalidated += () => sheet++;

        session.BeginRegionStroke(DirtId);
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.East));

        Assert.Equal(1, canvas);
        Assert.Equal(0, sheet);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanUndo);
        Assert.Same(catalog, vm.CurrentCatalog);

        Assert.True(session.CompleteRegionStroke());
        Assert.Equal(2, canvas);
        Assert.Equal(1, sheet);
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanUndo);
    }

    [Fact]
    public void CommitPending_NoChange_RaisesNoPropertyChanged()
    {
        var (vm, _, _) = CreateViewModel();
        vm.SelectedTerrain = Item(vm, GrassId);
        var changes = new List<string>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? string.Empty);

        vm.Name = "Grass";
        vm.ColorOverrideText = string.Empty;
        Assert.True(vm.CommitPending());
        Assert.Empty(changes);
    }

    [Fact]
    public void Committed_RaisesAffectedPropertiesOnly()
    {
        var (vm, _, _) = CreateViewModel();
        vm.SelectedTerrain = Item(vm, GrassId);
        var changes = new List<string>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? string.Empty);

        vm.Name = "Grass2";
        Assert.True(vm.CommitPending());

        Assert.Contains(nameof(TerrainEditorViewModel.Terrains), changes);
        Assert.Contains(nameof(TerrainEditorViewModel.SelectedTerrain), changes);
        Assert.Contains(nameof(TerrainEditorViewModel.IsDirty), changes);
        Assert.Contains(nameof(TerrainEditorViewModel.CanUndo), changes);
        Assert.Contains(nameof(TerrainEditorViewModel.Warnings), changes);
        Assert.DoesNotContain(nameof(TerrainEditorViewModel.CanRedo), changes);
        Assert.DoesNotContain(nameof(TerrainEditorViewModel.CanSave), changes);
        Assert.DoesNotContain(nameof(TerrainEditorViewModel.Errors), changes);
    }

    [Fact]
    public void AddTerrain_SelectsNewItem_AndDeleteRestoresSelection()
    {
        var (vm, session, _) = CreateViewModel();
        Assert.Equal(DirtId, vm.SelectedTerrain!.Id);

        var id = vm.AddTerrain();
        Assert.NotNull(id);
        Assert.Equal(id!.Value, vm.SelectedTerrain!.Id);
        Assert.Contains(vm.Terrains, item => item.Id == id.Value);

        Assert.True(vm.DeleteSelectedTerrain());
        Assert.NotEqual(id.Value, vm.SelectedTerrain!.Id);
        Assert.Equal(2, vm.Terrains.Count);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void AddTerrain_WithInvalidPendingText_IsBlocked()
    {
        var (vm, _, _) = CreateViewModel();
        var catalog = vm.CurrentCatalog;
        vm.SelectedTerrain = Item(vm, GrassId);
        vm.Name = string.Empty;

        Assert.Null(vm.AddTerrain());
        Assert.Same(catalog, vm.CurrentCatalog);
        Assert.Equal(2, vm.Terrains.Count);
        Assert.NotNull(vm.NameError);
    }

    [Fact]
    public void SelectionChange_RaisesExactMetadataPropertyNames()
    {
        var (vm, _, _) = CreateViewModel();
        vm.SelectedTerrain = Item(vm, DirtId);
        vm.Name = "Dirt2";
        vm.ColorOverrideText = "#000001";
        var changes = new List<string>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? string.Empty);

        vm.SelectedTerrain = Item(vm, GrassId);

        Assert.Contains(nameof(TerrainEditorViewModel.Name), changes);
        Assert.Contains(nameof(TerrainEditorViewModel.ColorOverrideText), changes);
        Assert.DoesNotContain("SyncPendingText", changes);
        Assert.Equal("Grass", vm.Name);
        Assert.Equal(string.Empty, vm.ColorOverrideText);
    }

    [Fact]
    public void TypingAfterValidationError_RaisesExactErrorPropertyNames()
    {
        var (vm, _, _) = CreateViewModel();
        vm.SelectedTerrain = Item(vm, GrassId);
        vm.Name = "Dirt";
        Assert.False(vm.CommitPending());
        Assert.NotNull(vm.NameError);
        vm.ColorOverrideText = "nope";
        Assert.False(vm.CommitPending());
        Assert.NotNull(vm.ColorError);

        var changes = new List<string>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? string.Empty);

        vm.Name = "Fresh";
        Assert.Equal(
            new[] { nameof(TerrainEditorViewModel.Name), nameof(TerrainEditorViewModel.NameError) },
            changes);
        Assert.Null(vm.NameError);

        changes.Clear();
        vm.ColorOverrideText = "#123456";
        Assert.Equal(
            new[] { nameof(TerrainEditorViewModel.ColorOverrideText), nameof(TerrainEditorViewModel.ColorError) },
            changes);
        Assert.Null(vm.ColorError);
    }

    [Fact]
    public void TryParseColorOverride_RejectsSignedHex()
    {
        foreach (var text in new[] { "#+12345", "#-12345" })
        {
            Assert.False(TerrainEditorViewModel.TryParseColorOverride(text, out _), text);
        }

        Assert.True(TerrainEditorViewModel.TryParseColorOverride("#123456", out var color));
        Assert.Equal(new TerrainColor(0x12, 0x34, 0x56), color);
    }

    [Fact]
    public void ErrorsAndWarnings_AreSurfacedSeparately()
    {
        var (vm, session, _) = CreateViewModel();
        Assert.Empty(vm.Errors);
        Assert.NotEmpty(vm.Warnings);
        Assert.Contains(vm.Warnings, warning => warning.Code == TerrainValidationCode.MissingCoveragePattern);

        Assert.True(session.RenameTerrain(DirtId, "grass"));

        Assert.Contains(vm.Errors, error => error.Code == TerrainValidationCode.DuplicateTerrainName);
        Assert.False(vm.CanSave);
        Assert.NotEmpty(vm.Warnings);
    }
}
