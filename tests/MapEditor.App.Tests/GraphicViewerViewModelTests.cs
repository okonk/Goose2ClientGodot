using System;
using System.Collections.Generic;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class GraphicViewerViewModelTests
{
    private const string SpriteJson = """
        { "tileSize": 32, "sheets": {
          "10": { "1": [0, 0, 4, 4], "2": [4, 0, 4, 4], "3": [4, 0, 4, 4] },
          "20": { "1": [0, 0, 4, 4], "2": [4, 0, 4, 4] },
          "30": { "1": [0, 0, 4, 4] },
          "40": { "1": [0, 0, 4, 4] }
        } }
        """;

    private const string AnimationJson = """
        { "version": 1,
          "sheets": {
            "10": { "categories": [ { "name": "Body", "id": 1 }, { "name": "Tiles" } ] },
            "20": { "categories": [ { "name": "Hair", "id": 2 } ] },
            "30": { "categories": [ { "name": "Spells" } ] }
          },
          "animations": [
            { "ownerSheet": 10, "id": 1, "fps": 8, "frames": [[10, 1], [10, 2]] },
            { "ownerSheet": 10, "id": 2, "fps": 8, "frames": [[10, 2], [10, 3]] },
            { "ownerSheet": 20, "id": 1, "fps": 8, "frames": [[20, 1], [20, 2]] }
          ] }
        """;

    private static (SpriteManifest Sprites, GraphicAssetCatalog Catalog) CreateAssets()
        => (SpriteManifest.Parse(SpriteJson),
            GraphicAssetCatalog.Create(SpriteManifest.Parse(SpriteJson), GraphicAnimationManifest.Parse(AnimationJson)));

    private static GraphicViewerViewModel CreateViewer()
    {
        (SpriteManifest sprites, GraphicAssetCatalog catalog) = CreateAssets();
        return new GraphicViewerViewModel(sprites, catalog);
    }

    [Fact]
    public void Initial_AllFilterUsesCatalogSheetsNotTileFiltered()
    {
        GraphicViewerViewModel viewer = CreateViewer();

        Assert.Equal(GraphicViewerCategoryFilter.All, viewer.Category);
        Assert.Equal(new[] { 10, 20, 30, 40 }, viewer.Sheets);
        Assert.Null(viewer.SelectedSheetId);
        Assert.Null(viewer.SelectedFrame);
        Assert.Null(viewer.SelectedAnimation);
        Assert.Equal(0, viewer.FrameIndex);
        Assert.False(viewer.IsPlaying);
        Assert.Equal(1.0, viewer.Zoom);
    }

    [Fact]
    public void Filters_AppearInDocumentedOrder()
    {
        Assert.Equal(
            new[]
            {
                GraphicViewerCategoryFilter.All,
                GraphicViewerCategoryFilter.Body,
                GraphicViewerCategoryFilter.Hair,
                GraphicViewerCategoryFilter.Eyes,
                GraphicViewerCategoryFilter.Chest,
                GraphicViewerCategoryFilter.Helm,
                GraphicViewerCategoryFilter.Legs,
                GraphicViewerCategoryFilter.Feet,
                GraphicViewerCategoryFilter.Hand,
                GraphicViewerCategoryFilter.Tiles,
                GraphicViewerCategoryFilter.Spells
            },
            Enum.GetValues<GraphicViewerCategoryFilter>());
    }

    [Fact]
    public void CategoryChange_SelectsFirstSheetOrNone()
    {
        GraphicViewerViewModel viewer = CreateViewer();

        viewer.Category = GraphicViewerCategoryFilter.Hair;
        Assert.Equal(new[] { 20 }, viewer.Sheets);
        Assert.Equal(20, viewer.SelectedSheetId);

        viewer.Category = GraphicViewerCategoryFilter.Body;
        Assert.Equal(new[] { 10 }, viewer.Sheets);
        Assert.Equal(10, viewer.SelectedSheetId);

        viewer.Category = GraphicViewerCategoryFilter.Eyes;
        Assert.Empty(viewer.Sheets);
        Assert.Null(viewer.SelectedSheetId);
    }

    [Fact]
    public void CategoryChange_ClearsFrameAndPlayback()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);
        Assert.True(viewer.TrySelectFrameAt(2, 2));
        Assert.True(viewer.IsPlaying);

        viewer.Category = GraphicViewerCategoryFilter.Hair;

        Assert.Equal(20, viewer.SelectedSheetId);
        Assert.Null(viewer.SelectedFrame);
        Assert.Empty(viewer.MatchingAnimations);
        Assert.Null(viewer.SelectedAnimation);
        Assert.Equal(0, viewer.FrameIndex);
        Assert.False(viewer.IsPlaying);
    }

    [Fact]
    public void TrySelectSheet_OnlyAcceptsSheetsInCurrentFilter()
    {
        GraphicViewerViewModel viewer = CreateViewer();

        Assert.True(viewer.TrySelectSheet(20));
        Assert.Equal(20, viewer.SelectedSheetId);

        Assert.False(viewer.TrySelectSheet(999));
        Assert.Equal(20, viewer.SelectedSheetId);

        viewer.Category = GraphicViewerCategoryFilter.Hair;
        Assert.Equal(20, viewer.SelectedSheetId);

        Assert.False(viewer.TrySelectSheet(10));
        Assert.Equal(20, viewer.SelectedSheetId);
    }

    [Fact]
    public void TrySelectSheet_SameSheetPreservesFrameState()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);
        Assert.True(viewer.TrySelectFrameAt(2, 2));
        Assert.True(viewer.IsPlaying);

        Assert.True(viewer.TrySelectSheet(10));

        Assert.Equal(new SpriteReference(10, 1), viewer.SelectedFrame!.Value.Reference);
        Assert.True(viewer.IsPlaying);
    }

    [Fact]
    public void SheetChange_PreservesZoomAndClearsFrameState()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);
        Assert.True(viewer.TrySelectFrameAt(2, 2));
        viewer.Zoom = 2.0;

        Assert.True(viewer.TrySelectSheet(20));

        Assert.Equal(2.0, viewer.Zoom);
        Assert.Null(viewer.SelectedFrame);
        Assert.Empty(viewer.MatchingAnimations);
        Assert.Null(viewer.SelectedAnimation);
        Assert.Equal(0, viewer.FrameIndex);
        Assert.False(viewer.IsPlaying);
    }

    [Fact]
    public void HitTest_AppliesInverseZoomAndHalfOpenRects()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);
        viewer.Zoom = 2.0;

        Assert.True(viewer.TrySelectFrameAt(0, 0));
        Assert.Equal(new SpriteReference(10, 1), viewer.SelectedFrame!.Value.Reference);

        Assert.True(viewer.TrySelectFrameAt(2, 2));
        Assert.Equal(new SpriteReference(10, 1), viewer.SelectedFrame!.Value.Reference);

        Assert.True(viewer.TrySelectFrameAt(8, 2));
        Assert.Equal(new SpriteReference(10, 2), viewer.SelectedFrame!.Value.Reference);

        Assert.False(viewer.TrySelectFrameAt(2, 8));
        Assert.Equal(new SpriteReference(10, 2), viewer.SelectedFrame!.Value.Reference);

        Assert.False(viewer.TrySelectFrameAt(100, 100));
        Assert.Equal(new SpriteReference(10, 2), viewer.SelectedFrame!.Value.Reference);
    }

    [Fact]
    public void HitTest_OverlappingFramesChooseLowestGraphicId()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);

        Assert.True(viewer.TrySelectFrameAt(4, 1));

        Assert.Equal(new SpriteReference(10, 2), viewer.SelectedFrame!.Value.Reference);
    }

    [Fact]
    public void HitTest_WithoutSheetOrFramesFails()
    {
        GraphicViewerViewModel viewer = CreateViewer();

        Assert.False(viewer.TrySelectFrameAt(2, 2));
        Assert.Null(viewer.SelectedFrame);
    }

    [Fact]
    public void FrameSelection_LoadsMappingsAndMatchingAnimations()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);

        Assert.True(viewer.TrySelectFrameAt(2, 2));

        Assert.Equal(
            new[]
            {
                new GraphicCategoryMapping(GraphicCategory.Body, 1),
                new GraphicCategoryMapping(GraphicCategory.Tiles, null)
            },
            viewer.Mappings);
        Assert.Single(viewer.MatchingAnimations);
        Assert.Equal(new GraphicAnimationKey(10, 1), viewer.MatchingAnimations[0].Key);
    }

    [Fact]
    public void FrameSelection_FirstMatchingAnimationSelectedAndPlaying()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);

        Assert.True(viewer.TrySelectFrameAt(5, 2));

        Assert.Equal(2, viewer.MatchingAnimations.Count);
        Assert.Equal(new GraphicAnimationKey(10, 1), viewer.SelectedAnimation!.Key);
        Assert.Equal(0, viewer.FrameIndex);
        Assert.True(viewer.IsPlaying);
        Assert.Equal(new SpriteReference(10, 1), viewer.CurrentFrame);
    }

    [Fact]
    public void FrameSelection_NoAnimationRemainsSelectedAndStopped()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(30);

        Assert.True(viewer.TrySelectFrameAt(2, 2));

        Assert.Equal(new SpriteReference(30, 1), viewer.SelectedFrame!.Value.Reference);
        Assert.Empty(viewer.MatchingAnimations);
        Assert.Null(viewer.SelectedAnimation);
        Assert.Null(viewer.CurrentFrame);
        Assert.False(viewer.IsPlaying);
    }

    [Fact]
    public void SelectAnimation_ResetsPositionAndStarts()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);
        Assert.True(viewer.TrySelectFrameAt(5, 2));
        viewer.StepNext();
        Assert.Equal(1, viewer.FrameIndex);

        viewer.SelectAnimation(viewer.MatchingAnimations[1]);

        Assert.Equal(new GraphicAnimationKey(10, 2), viewer.SelectedAnimation!.Key);
        Assert.Equal(0, viewer.FrameIndex);
        Assert.True(viewer.IsPlaying);
        Assert.Equal(new SpriteReference(10, 2), viewer.CurrentFrame);
    }

    [Fact]
    public void StepNextAndPrevious_WrapAround()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);
        Assert.True(viewer.TrySelectFrameAt(2, 2));

        Assert.Equal(0, viewer.FrameIndex);
        viewer.StepNext();
        Assert.Equal(1, viewer.FrameIndex);
        viewer.StepNext();
        Assert.Equal(0, viewer.FrameIndex);
        viewer.StepPrevious();
        Assert.Equal(1, viewer.FrameIndex);
        viewer.StepPrevious();
        Assert.Equal(0, viewer.FrameIndex);
    }

    [Fact]
    public void Tick_AdvancesByElapsedAndWraps()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);
        Assert.True(viewer.TrySelectFrameAt(2, 2));

        viewer.Tick(0.0625);
        Assert.Equal(0, viewer.FrameIndex);
        viewer.Tick(0.0625);
        Assert.Equal(1, viewer.FrameIndex);
        viewer.Tick(0.3);
        Assert.Equal(1, viewer.FrameIndex);
        viewer.Tick(0.1);
        Assert.Equal(0, viewer.FrameIndex);
    }

    [Fact]
    public void Tick_PausedOrWithoutAnimationDoesNothing()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);
        Assert.True(viewer.TrySelectFrameAt(2, 2));
        viewer.Pause();

        viewer.Tick(1.0);
        Assert.Equal(0, viewer.FrameIndex);

        viewer.TrySelectSheet(30);
        Assert.True(viewer.TrySelectFrameAt(2, 2));
        viewer.Tick(1.0);
        Assert.Equal(0, viewer.FrameIndex);
    }

    [Fact]
    public void PauseAndPlay_TogglePlayback()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);
        Assert.True(viewer.TrySelectFrameAt(2, 2));
        Assert.True(viewer.IsPlaying);

        viewer.Pause();
        Assert.False(viewer.IsPlaying);
        viewer.Play();
        Assert.True(viewer.IsPlaying);
    }

    [Fact]
    public void UnavailableBind_StopsPlaybackAndResets()
    {
        (SpriteManifest sprites, GraphicAssetCatalog catalog) = CreateAssets();
        GraphicViewerViewModel viewer = new(sprites, catalog);
        viewer.TrySelectSheet(10);
        Assert.True(viewer.TrySelectFrameAt(2, 2));
        viewer.Zoom = 3.0;

        viewer.Bind(sprites, null);

        Assert.False(viewer.IsPlaying);
        Assert.Null(viewer.SelectedSheetId);
        Assert.Null(viewer.SelectedFrame);
        Assert.Null(viewer.SelectedAnimation);
        Assert.Equal(0, viewer.FrameIndex);
        Assert.Equal(GraphicViewerCategoryFilter.All, viewer.Category);
        Assert.Equal(1.0, viewer.Zoom);
    }

    [Fact]
    public void Unbind_ResetsAllState()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);
        Assert.True(viewer.TrySelectFrameAt(2, 2));

        viewer.Unbind();

        Assert.Empty(viewer.Sheets);
        Assert.Null(viewer.SelectedSheetId);
        Assert.Null(viewer.SelectedFrame);
        Assert.Empty(viewer.Mappings);
        Assert.Null(viewer.SelectedAnimation);
        Assert.False(viewer.IsPlaying);
    }

    [Fact]
    public void Zoom_ClampsToBoundsAnd100PercentIsExact()
    {
        GraphicViewerViewModel viewer = CreateViewer();

        viewer.Zoom = 0.01;
        Assert.Equal(0.25, viewer.Zoom);
        viewer.Zoom = 100.0;
        Assert.Equal(8.0, viewer.Zoom);
        viewer.Zoom = 4.0;
        viewer.ResetZoom();
        Assert.Equal(1.0, viewer.Zoom);
    }

    [Fact]
    public void FitToViewport_UsesSmallerScaleAndClamps()
    {
        GraphicViewerViewModel viewer = CreateViewer();

        viewer.FitToViewport(16, 8, 8, 4);
        Assert.Equal(2.0, viewer.Zoom);
        viewer.FitToViewport(4, 100, 8, 4);
        Assert.Equal(0.5, viewer.Zoom);
        viewer.FitToViewport(100, 100, 8, 4);
        Assert.Equal(8.0, viewer.Zoom);
        viewer.FitToViewport(2, 2, 8, 4);
        Assert.Equal(0.25, viewer.Zoom);
    }

    [Fact]
    public void FitToViewport_UsesImageDimensionsNotFrameExtents()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.TrySelectSheet(10);

        viewer.FitToViewport(100, 50, 50, 25);

        Assert.Equal(2.0, viewer.Zoom);
    }

    [Fact]
    public void FitToViewport_WithNonPositiveOrNaNDimensionsIsANoOp()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        viewer.Zoom = 3.0;

        viewer.FitToViewport(100, 100, 0, 4);
        viewer.FitToViewport(100, 100, -1, 4);
        viewer.FitToViewport(100, 100, double.NaN, 4);
        viewer.FitToViewport(100, 100, 8, double.NaN);
        viewer.FitToViewport(0, 100, 8, 4);

        Assert.Equal(3.0, viewer.Zoom);
    }

    [Fact]
    public void ViewerState_DoesNotMutateMapDocument()
    {
        MapDocument document = MapDocument.Create(4, 4);
        byte[] before = MapCodec.Encode(document);
        GraphicViewerViewModel viewer = CreateViewer();

        viewer.Category = GraphicViewerCategoryFilter.Hair;
        viewer.TrySelectSheet(20);
        viewer.TrySelectFrameAt(2, 2);
        viewer.Tick(0.5);
        viewer.StepNext();
        viewer.Zoom = 2.0;
        viewer.Unbind();

        Assert.Equal(before, MapCodec.Encode(document));
    }

    [Fact]
    public void PropertyChanged_RaisesForChangedState()
    {
        GraphicViewerViewModel viewer = CreateViewer();
        List<string> raised = new();
        viewer.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        viewer.Category = GraphicViewerCategoryFilter.Hair;
        Assert.Contains(nameof(GraphicViewerViewModel.Category), raised);
        Assert.Contains(nameof(GraphicViewerViewModel.Sheets), raised);
        Assert.Contains(nameof(GraphicViewerViewModel.SelectedSheetId), raised);
        raised.Clear();

        viewer.Zoom = 2.0;
        Assert.Contains(nameof(GraphicViewerViewModel.Zoom), raised);
        raised.Clear();

        viewer.Category = GraphicViewerCategoryFilter.All;
        raised.Clear();

        viewer.TrySelectSheet(20);
        Assert.Contains(nameof(GraphicViewerViewModel.Frames), raised);
        Assert.Contains(nameof(GraphicViewerViewModel.Mappings), raised);
    }
}
