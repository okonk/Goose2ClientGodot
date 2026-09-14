using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MapEditor.App.Controls;
using MapEditor.App.Rendering;
using MapEditor.App.Terrain;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class TerrainSheetControlTests
{
    private static readonly Guid GrassId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DirtId = new("00000000-0000-0000-0000-000000000002");
    private static readonly TerrainColor GrassOverride = new(0x33, 0x66, 0x99);

    private const string ManifestJson = """
        { "tileSize": 32, "sheets": { "1": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32] }, "2": { "20": [0, 0, 32, 32] } } }
        """;

    private sealed record Harness(
        TerrainSheetControl Control,
        TerrainEditorViewModel ViewModel,
        CountingSpriteSheetLoader Loader,
        Window Window,
        List<AvaloniaSpriteSheetImage> Images);

    private static List<TerrainDefinition> CreateTerrains() => new()
    {
        new(GrassId, "Grass", GrassOverride),
        new(DirtId, "Dirt", null)
    };

    private static List<TerrainGraphicDefinition> CreateGraphics() => new()
    {
        new(new TerrainGraphicReference(1, 10), new TerrainPattern(Center: GrassId, North: DirtId)),
        new(new TerrainGraphicReference(1, 11), new TerrainPattern(South: GrassId))
    };

    private static Harness Create(
        string manifestJson = ManifestJson,
        List<TerrainGraphicDefinition>? graphics = null,
        double zoom = 1.0,
        bool inScrollViewer = false,
        bool missingSheets = false)
    {
        SpriteManifest manifest = SpriteManifest.Parse(manifestJson);
        TerrainCatalog catalog = new(CreateTerrains(), graphics ?? CreateGraphics());
        TerrainEditorViewModel viewModel = new(new TerrainEditorSession(catalog, manifest), manifest, manifest.SheetIds);
        List<AvaloniaSpriteSheetImage> images = new();
        CountingSpriteSheetLoader loader = new(path =>
        {
            if (missingSheets)
            {
                return SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.NotFound, $"Sheet file not found: {path}.");
            }

            AvaloniaSpriteSheetImage image = new(new Bitmap(new MemoryStream(AssetFixture.PngSheet.Create(64, 64))));
            images.Add(image);
            return SpriteSheetLoadResult.Success(image);
        });
        TerrainSheetControl control = new(viewModel, "/assets", loader);
        viewModel.Zoom = zoom;
        Window window = new();
        if (inScrollViewer)
        {
            var scroller = new ScrollViewer
            {
                Width = 100,
                Height = 100,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                Content = control
            };
            window.Content = scroller;
            window.Width = 120;
            window.Height = 120;
        }
        else
        {
            window.Content = control;
            window.Width = 200;
            window.Height = 200;
        }

        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new Harness(control, viewModel, loader, window, images);
    }

    private static TerrainColor ColorOf(RecordingMapDrawTarget.PolygonDraw polygon)
    {
        Color color = Assert.IsType<SolidColorBrush>(polygon.Fill).Color;
        return new TerrainColor(color.R, color.G, color.B);
    }

    private static TerrainGraphicDefinition Graphic(Harness harness, int sheet, int graphic)
        => harness.ViewModel.CurrentCatalog.Graphics.Single(entry => entry.Reference.Sheet == sheet && entry.Reference.Graphic == graphic);

    private static void SelectGrass(Harness harness)
        => harness.ViewModel.SelectedTerrain = harness.ViewModel.Terrains.Single(item => item.Id == GrassId);

    [AvaloniaFact]
    public void Drag_LeftPaint_CommitsSingleCommandOnRelease()
    {
        Harness harness = Create();
        SelectGrass(harness);

        harness.Window.MouseDown(new Point(16, 4), MouseButton.Left, RawInputModifiers.None);
        Assert.True(harness.Control.IsPainting);
        Assert.False(harness.ViewModel.IsDirty);
        Assert.False(harness.ViewModel.CanUndo);

        harness.Window.MouseMove(new Point(20, 4), RawInputModifiers.None);
        Assert.False(harness.ViewModel.IsDirty);

        harness.Window.MouseUp(new Point(20, 4), MouseButton.Left, RawInputModifiers.None);
        Assert.False(harness.Control.IsPainting);
        Assert.Equal(GrassId, Graphic(harness, 1, 10).Pattern.North);
        Assert.True(harness.ViewModel.IsDirty);
        Assert.True(harness.ViewModel.CanUndo);

        Assert.True(harness.ViewModel.Undo());
        Assert.Equal(DirtId, Graphic(harness, 1, 10).Pattern.North);
        Assert.False(harness.ViewModel.IsDirty);
    }

    [AvaloniaFact]
    public void SecondPointer_DoesNotExtendOrCommitStroke()
    {
        Harness harness = Create();
        SelectGrass(harness);

        harness.Window.MouseDown(new Point(16, 4), MouseButton.Left, RawInputModifiers.None);
        Assert.True(harness.Control.IsPainting);
        Assert.False(harness.ViewModel.IsDirty);
        Assert.False(harness.ViewModel.CanUndo);

        IPointer second = new Pointer(2, PointerType.Mouse, false);

        harness.Control.RaiseEvent(new PointerEventArgs(
            InputElement.PointerMovedEvent,
            harness.Control,
            second,
            harness.Control,
            new Point(48, 4),
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None));
        harness.Control.RaiseEvent(new PointerReleasedEventArgs(
            harness.Control,
            second,
            harness.Control,
            new Point(48, 4),
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            MouseButton.Left));

        Assert.True(harness.Control.IsPainting);
        Assert.False(harness.ViewModel.IsDirty);
        Assert.False(harness.ViewModel.CanUndo);

        harness.Window.MouseUp(new Point(16, 4), MouseButton.Left, RawInputModifiers.None);

        Assert.False(harness.Control.IsPainting);
        Assert.True(harness.ViewModel.IsDirty);
        Assert.True(harness.ViewModel.CanUndo);
        Assert.Equal(GrassId, Graphic(harness, 1, 10).Pattern.North);
        Assert.Null(Graphic(harness, 1, 11).Pattern.North);

        Assert.True(harness.ViewModel.Undo());
        Assert.Equal(DirtId, Graphic(harness, 1, 10).Pattern.North);
        Assert.False(harness.ViewModel.IsDirty);
    }

    [AvaloniaFact]
    public void Drag_ShiftLeft_ClearsRegionAndCommits()
    {
        Harness harness = Create();
        SelectGrass(harness);

        harness.Window.MouseDown(new Point(16, 4), MouseButton.Left, RawInputModifiers.Shift);
        Assert.True(harness.Control.IsPainting);

        harness.Window.MouseUp(new Point(16, 4), MouseButton.Left, RawInputModifiers.Shift);
        Assert.Null(Graphic(harness, 1, 10).Pattern.North);
        Assert.True(harness.ViewModel.IsDirty);
        Assert.True(harness.ViewModel.CanUndo);

        Assert.True(harness.ViewModel.Undo());
        Assert.Equal(DirtId, Graphic(harness, 1, 10).Pattern.North);
    }

    [AvaloniaFact]
    public void Drag_Escape_CancelsAndRestoresPreview()
    {
        Harness harness = Create();
        SelectGrass(harness);

        harness.Window.MouseDown(new Point(16, 4), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(20, 4), RawInputModifiers.None);
        Assert.True(harness.Control.IsPainting);
        Assert.False(harness.ViewModel.IsDirty);

        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(harness.Control.IsPainting);
        Assert.Equal(DirtId, Graphic(harness, 1, 10).Pattern.North);
        Assert.False(harness.ViewModel.IsDirty);
        Assert.False(harness.ViewModel.CanUndo);

        harness.Window.MouseUp(new Point(20, 4), MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(DirtId, Graphic(harness, 1, 10).Pattern.North);
        Assert.False(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public void CaptureLoss_RestoresDraftAndHistory()
    {
        Harness harness = Create();
        SelectGrass(harness);

        harness.Window.MouseDown(new Point(16, 4), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(20, 4), RawInputModifiers.None);
        Assert.True(harness.Control.IsPainting);
        Assert.False(harness.ViewModel.IsDirty);
        Assert.False(harness.ViewModel.CanUndo);

        harness.Window.Content = new Border();

        Assert.False(harness.Control.IsPainting);
        Assert.Equal(DirtId, Graphic(harness, 1, 10).Pattern.North);
        Assert.False(harness.ViewModel.IsDirty);
        Assert.False(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public void Drag_SparseMovement_VisitsEveryCrossedRegion()
    {
        Harness harness = Create();
        SelectGrass(harness);

        harness.Window.MouseDown(new Point(16, 4), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(48, 4), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(48, 4), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(GrassId, Graphic(harness, 1, 10).Pattern.North);
        Assert.Equal(GrassId, Graphic(harness, 1, 11).Pattern.North);
        Assert.True(harness.ViewModel.CanUndo);

        Assert.True(harness.ViewModel.Undo());
        Assert.Equal(DirtId, Graphic(harness, 1, 10).Pattern.North);
        Assert.Null(Graphic(harness, 1, 11).Pattern.North);
        Assert.False(harness.ViewModel.IsDirty);

        Harness zoomed = Create(zoom: 2.0);
        SelectGrass(zoomed);

        zoomed.Window.MouseDown(new Point(32, 8), MouseButton.Left, RawInputModifiers.None);
        zoomed.Window.MouseMove(new Point(96, 8), RawInputModifiers.None);
        zoomed.Window.MouseUp(new Point(96, 8), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(GrassId, Graphic(zoomed, 1, 10).Pattern.North);
        Assert.Equal(GrassId, Graphic(zoomed, 1, 11).Pattern.North);
    }

    [AvaloniaFact]
    public void Drag_ActiveGesture_DisablesStateChangesAndUsesCapturedValues()
    {
        Harness harness = Create(zoom: 2.0);
        SelectGrass(harness);

        harness.Window.MouseDown(new Point(32, 8), MouseButton.Left, RawInputModifiers.None);
        Assert.True(harness.Control.IsPainting);

        harness.ViewModel.Zoom = 1.0;
        harness.ViewModel.SelectedSheet = 2;
        harness.ViewModel.SelectedTerrain = harness.ViewModel.Terrains.Single(item => item.Id == DirtId);
        Dispatcher.UIThread.RunJobs();

        harness.Window.MouseWheel(new Point(50, 50), new Vector(0, -1), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1.0, harness.ViewModel.Zoom);

        harness.Window.MouseMove(new Point(40, 8), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(40, 8), MouseButton.Left, RawInputModifiers.None);
        Assert.False(harness.Control.IsPainting);

        Assert.Equal(GrassId, Graphic(harness, 1, 10).Pattern.North);
        Assert.True(harness.ViewModel.IsDirty);

        Assert.True(harness.ViewModel.Undo());
        Assert.Equal(DirtId, Graphic(harness, 1, 10).Pattern.North);
        Assert.False(harness.ViewModel.IsDirty);
    }

    [AvaloniaFact]
    public void Drag_RepeatRegion_CommitsNoCommand()
    {
        Harness harness = Create();
        SelectGrass(harness);

        harness.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);
        Assert.True(harness.Control.IsPainting);
        harness.Window.MouseMove(new Point(20, 16), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(20, 16), MouseButton.Left, RawInputModifiers.None);

        Assert.False(harness.ViewModel.IsDirty);
        Assert.False(harness.ViewModel.CanUndo);
        Assert.Equal(GrassId, Graphic(harness, 1, 10).Pattern.Center);
    }

    [AvaloniaFact]
    public void Press_WithoutImageRegionOrSelection_StartsNoCapture()
    {
        Harness missing = Create(missingSheets: true);
        SelectGrass(missing);
        missing.Window.MouseDown(new Point(16, 4), MouseButton.Left, RawInputModifiers.None);
        Assert.False(missing.Control.IsPainting);
        missing.Window.MouseUp(new Point(16, 4), MouseButton.Left, RawInputModifiers.None);
        Assert.False(missing.ViewModel.IsDirty);

        Harness noRegion = Create();
        SelectGrass(noRegion);
        noRegion.Window.MouseDown(new Point(16, 48), MouseButton.Left, RawInputModifiers.None);
        Assert.False(noRegion.Control.IsPainting);
        noRegion.Window.MouseUp(new Point(16, 48), MouseButton.Left, RawInputModifiers.None);
        Assert.False(noRegion.ViewModel.IsDirty);
        Assert.False(noRegion.ViewModel.CanUndo);

        Harness noSelection = Create();
        noSelection.ViewModel.SelectedTerrain = null;
        noSelection.Window.MouseDown(new Point(16, 4), MouseButton.Left, RawInputModifiers.None);
        Assert.False(noSelection.Control.IsPainting);
        noSelection.Window.MouseUp(new Point(16, 4), MouseButton.Left, RawInputModifiers.None);
        Assert.False(noSelection.ViewModel.IsDirty);
    }

    [AvaloniaFact]
    public void Drag_LeaveControlPreservesLastPointAndReentryInterpolates()
    {
        Harness harness = Create();
        SelectGrass(harness);

        harness.Window.MouseDown(new Point(16, 4), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(150, 150), RawInputModifiers.None);
        Assert.True(harness.Control.IsPainting);
        harness.Window.MouseMove(new Point(48, 4), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(48, 4), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(GrassId, Graphic(harness, 1, 10).Pattern.North);
        Assert.Equal(GrassId, Graphic(harness, 1, 11).Pattern.North);
    }

    [AvaloniaFact]
    public void Dispose_WithActiveStroke_CancelsPreview()
    {
        Harness harness = Create();
        SelectGrass(harness);

        harness.Window.MouseDown(new Point(16, 4), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(20, 4), RawInputModifiers.None);
        Assert.True(harness.Control.IsPainting);

        harness.Control.Dispose();

        Assert.Equal(DirtId, Graphic(harness, 1, 10).Pattern.North);
        Assert.False(harness.ViewModel.IsDirty);
        Assert.False(harness.ViewModel.CanUndo);
    }

    [Fact]
    public void Line_SamplesCeilOfMaxDeltaStepsIncludingEndpoints()
    {
        List<Point> samples = new();
        TerrainSheetLine.AppendSamples(new Point(0, 0), new Point(31, 5), samples);
        Assert.Equal(32, samples.Count);
        Assert.Equal(new Point(0, 0), samples[0]);
        Assert.Equal(new Point(31, 5), samples[^1]);

        samples.Clear();
        TerrainSheetLine.AppendSamples(new Point(7.5, 3.25), new Point(7.5, 3.25), samples);
        Assert.Equal(new[] { new Point(7.5, 3.25) }, samples);
    }

    [AvaloniaFact]
    public void Render_DrawsImageBeforeAlphaCcPolygons()
    {
        Harness harness = Create();
        RecordingMapDrawTarget target = new();

        harness.Control.RenderSheet(target);

        Assert.Single(target.Images);
        Assert.Equal(new Rect(0, 0, 64, 64), target.Images[0].Source);
        Assert.Equal(new Rect(0, 0, 64, 64), target.Images[0].Destination);
        Assert.Equal(BitmapInterpolationMode.None, RenderOptions.GetBitmapInterpolationMode(harness.Control));
        Assert.Equal(3, target.Polygons.Count);

        (TerrainPeer Peer, Point Origin, TerrainColor Color)[] expected =
        {
            (TerrainPeer.Center, new Point(0, 0), GrassOverride),
            (TerrainPeer.North, new Point(0, 0), TerrainColor.Derive(DirtId)),
            (TerrainPeer.South, new Point(32, 0), GrassOverride)
        };
        for (int i = 0; i < expected.Length; i++)
        {
            RecordingMapDrawTarget.PolygonDraw polygon = target.Polygons[i];
            Assert.Equal(TerrainRegionGeometry.ToScreenPolygon(expected[i].Peer, expected[i].Origin, 1.0), polygon.Points);
            Assert.Null(polygon.Stroke);
            Assert.Equal(0xCC, Assert.IsType<SolidColorBrush>(polygon.Fill).Color.A);
            Assert.Equal(expected[i].Color, ColorOf(polygon));
        }

        harness.ViewModel.Zoom = TerrainEditorViewModel.MinZoom;
        target.Images.Clear();
        target.Polygons.Clear();
        harness.Control.RenderSheet(target);

        Assert.Equal(new Rect(0, 0, 16, 16), target.Images[0].Destination);
        Assert.Equal(TerrainRegionGeometry.ToScreenPolygon(TerrainPeer.Center, new Point(0, 0), TerrainEditorViewModel.MinZoom), target.Polygons[0].Points);

        harness.ViewModel.Zoom = TerrainEditorViewModel.MaxZoom;
        target.Images.Clear();
        target.Polygons.Clear();
        harness.Control.RenderSheet(target);

        Assert.Equal(new Rect(0, 0, 512, 512), target.Images[0].Destination);
        Assert.Equal(TerrainRegionGeometry.ToScreenPolygon(TerrainPeer.South, new Point(32 * TerrainEditorViewModel.MaxZoom, 0), TerrainEditorViewModel.MaxZoom), target.Polygons[2].Points);
    }

    [AvaloniaFact]
    public void Render_Non32Frame_HasNoOverlayOrHitTarget()
    {
        const string manifestJson = """
            { "tileSize": 32, "sheets": { "1": { "10": [0, 0, 32, 32], "30": [0, 32, 64, 64] } } }
            """;
        List<TerrainGraphicDefinition> graphics = new()
        {
            new(new TerrainGraphicReference(1, 10), new TerrainPattern(Center: GrassId)),
            new(new TerrainGraphicReference(1, 30), new TerrainPattern(Center: GrassId))
        };
        Harness harness = Create(manifestJson, graphics);

        Assert.Equal(new[] { 10 }, harness.ViewModel.EligibleFrames.Select(frame => frame.Reference.Graphic));
        RecordingMapDrawTarget target = new();
        harness.Control.RenderSheet(target);

        Assert.Single(target.Images);
        Assert.Single(target.Polygons);
        Assert.Equal(TerrainRegionGeometry.ToScreenPolygon(TerrainPeer.Center, new Point(0, 0), 1.0), target.Polygons[0].Points);
    }

    [AvaloniaFact]
    public void Render_DrawsOnlyAuthoredPeersIncludingCenterlessAssignments()
    {
        List<TerrainGraphicDefinition> graphics = new()
        {
            new(new TerrainGraphicReference(1, 10), new TerrainPattern(NorthWest: DirtId)),
            new(new TerrainGraphicReference(1, 11), new TerrainPattern(East: GrassId))
        };
        Harness harness = Create(graphics: graphics);
        RecordingMapDrawTarget target = new();

        harness.Control.RenderSheet(target);

        Assert.Single(target.Images);
        Assert.Equal(2, target.Polygons.Count);
        Assert.Equal(TerrainRegionGeometry.ToScreenPolygon(TerrainPeer.NorthWest, new Point(0, 0), 1.0), target.Polygons[0].Points);
        Assert.Equal(TerrainColor.Derive(DirtId), ColorOf(target.Polygons[0]));
        Assert.Equal(TerrainRegionGeometry.ToScreenPolygon(TerrainPeer.East, new Point(32, 0), 1.0), target.Polygons[1].Points);
        Assert.Equal(GrassOverride, ColorOf(target.Polygons[1]));
    }

    [AvaloniaFact]
    public void SelectSheet_DisposesPreviousImageExactlyOnce()
    {
        Harness harness = Create();

        Assert.Single(harness.Images);
        Assert.Equal(0, harness.Images[0].DisposeCount);

        harness.ViewModel.SelectedSheet = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, harness.Loader.CallCount);
        Assert.Equal(1, harness.Images[0].DisposeCount);
        Assert.Equal(0, harness.Images[1].DisposeCount);

        harness.Control.Dispose();
        harness.Control.Dispose();

        Assert.Equal(1, harness.Images[0].DisposeCount);
        Assert.Equal(1, harness.Images[1].DisposeCount);
    }

    [AvaloniaFact]
    public void Render_WithoutImage_DrawsNothingMeasuresZeroAndKeepsSelectionAvailable()
    {
        Harness harness = Create(missingSheets: true);

        Assert.Null(harness.Control.Images.Image);
        Assert.Contains("/assets/sheets/1.png", harness.Control.Images.Diagnostic);
        Assert.Equal(1, harness.ViewModel.SelectedSheet);

        RecordingMapDrawTarget target = new();
        harness.Control.RenderSheet(target);
        Assert.Empty(target.Images);
        Assert.Empty(target.Polygons);

        harness.Control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(new Size(0, 0), harness.Control.DesiredSize);

        harness.ViewModel.SelectedSheet = 2;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, harness.ViewModel.SelectedSheet);
        Assert.Contains("/assets/sheets/2.png", harness.Control.Images.Diagnostic);
    }

    [AvaloniaFact]
    public void DesiredSize_TracksImageAndZoom()
    {
        Harness harness = Create();

        harness.Control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(new Size(64, 64), harness.Control.DesiredSize);

        harness.ViewModel.Zoom = 2.0;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Size(128, 128), harness.Control.DesiredSize);

        harness.ViewModel.Zoom = TerrainEditorViewModel.MinZoom;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Size(16, 16), harness.Control.DesiredSize);
    }

    [AvaloniaFact]
    public void CtrlWheel_ZoomsBoundedAndPlainWheel_LeavesScrollingToParent()
    {
        Harness harness = Create(zoom: 4.0, inScrollViewer: true);
        ScrollViewer scroller = (ScrollViewer)harness.Window.Content!;
        scroller.Offset = new Vector(0, 100);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Vector(0, 100), scroller.Offset);

        harness.Window.MouseWheel(new Point(50, 50), new Vector(0, -1), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.ViewModel.Zoom < 4.0);
        Assert.Equal(new Vector(0, 100), scroller.Offset);

        double zoomAfterCtrl = harness.ViewModel.Zoom;
        harness.Window.MouseWheel(new Point(50, 50), new Vector(0, 1), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(zoomAfterCtrl, harness.ViewModel.Zoom);
        Assert.NotEqual(new Vector(0, 100), scroller.Offset);

        for (int i = 0; i < 30; i++)
        {
            harness.Window.MouseWheel(new Point(50, 50), new Vector(0, -1), RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(TerrainEditorViewModel.MinZoom, harness.ViewModel.Zoom);
    }

    [AvaloniaFact]
    public void Dispose_DetachesSubscriptionsAndDisposesImageExactlyOnce()
    {
        Harness harness = Create();

        harness.Control.Dispose();
        int calls = harness.Loader.CallCount;

        harness.ViewModel.Zoom = 4.0;
        harness.ViewModel.SelectedSheet = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(calls, harness.Loader.CallCount);
        Assert.Equal(1, harness.Images[0].DisposeCount);

        harness.Control.Dispose();
        Assert.Equal(1, harness.Images[0].DisposeCount);
    }
}
