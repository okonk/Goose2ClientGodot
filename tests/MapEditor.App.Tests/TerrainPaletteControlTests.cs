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
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MapEditor.App.Controls;
using MapEditor.App.Documents;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;
using MapEditor.Rendering.Terrain;
using Xunit;

namespace MapEditor.App.Tests;

public class TerrainPaletteControlTests
{
    private sealed class Harness : IDisposable
    {
        private readonly string _directory;
        private bool _disposed;

        internal Harness(params (string Name, int Graphic)[] definitions)
        {
            _directory = Directory.CreateTempSubdirectory("terrain-palette-").FullName;
            Workspace = new WorkspaceViewModel(new FakeEditorDialogs(), new MapFileStore());
            ViewModel = Workspace.ActiveDocument;
            Assets = new AssetContextController(Workspace, new AppSettingsStore(Path.Combine(_directory, "settings.json")));
            TerrainAssetLoadResult result = TerrainTestData.Result(definitions);
            TerrainReplacementPlan plan = Assets.PrepareTerrainReplacement(Assets.Current, result).Plan!;
            Assets.PublishTerrain(plan);
            Palette = new TerrainPaletteControl(ViewModel, Assets, (context, reference) =>
            {
                Resolved.Add(reference);
                return Resolution(context, reference);
            })
            {
                Width = 240,
                Height = 116,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            Bar = new ScrollBar { Orientation = Orientation.Vertical };
            Grid host = new();
            host.Children.Add(Palette);
            host.Children.Add(Bar);
            Window = new Window { Content = host, Width = 300, Height = 200 };
            Window.Show();
            Palette.BindScrollBar(Bar);
            Dispatcher.UIThread.RunJobs();
            Resolved.Clear();
        }

        internal WorkspaceViewModel Workspace { get; }
        internal MapDocumentViewModel ViewModel { get; }
        internal AssetContextController Assets { get; }
        internal TerrainPaletteControl Palette { get; }
        internal ScrollBar Bar { get; }
        internal Window Window { get; }
        internal List<SpriteReference> Resolved { get; } = new();
        internal Func<AssetContext, SpriteReference, SpriteResolution> Resolution { get; set; }
            = (context, reference) => Failure(SpriteResolutionStatus.MissingSheetFile, reference, "sheet file is missing");

        internal static SpriteResolution Failure(SpriteResolutionStatus status, SpriteReference reference, string diagnostic)
            => new(status, reference, null, default, diagnostic);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Window.Content = null;
            Palette.Dispose();
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assets.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    [AvaloniaFact]
    public void TerrainPalette_RendersEnabledSetsWithRepresentativeTopologyAndWarning()
    {
        using Harness harness = new(("Grass", 10), ("Water", 20));
        RecordingMapDrawTarget target = new();

        harness.Palette.RenderPalette(target);

        Assert.Equal(new[] { new SpriteReference(1, 10), new SpriteReference(1, 20) }, harness.Resolved);
        Assert.Contains(target.Texts, text => text.Text == "Grass");
        Assert.Contains(target.Texts, text => text.Text == "4-way · sheet file is missing");
        Assert.Equal(3, target.Rectangles.Count);
        Assert.Equal(4, target.Lines.Count);
    }

    [AvaloniaFact]
    public void TerrainPalette_ClickSelectsTerrainAndActivatesToolWithoutEditingMap()
    {
        using Harness harness = new(("Grass", 10), ("Water", 20));
        byte[] before = MapCodec.Encode(harness.ViewModel.Session.Document);
        string expected = harness.Assets.Current.Terrain.Runtime!.EnabledSets[1].Id;

        harness.Window.MouseDown(new Point(20, TerrainPaletteControl.CellHeight + 20), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(expected, harness.ViewModel.SelectedTerrainId);
        Assert.Equal(AssetPaletteMode.Terrain, harness.ViewModel.PaletteMode);
        Assert.Equal(MapEditTool.Terrain, harness.ViewModel.ActiveTool);
        Assert.Equal(before, MapCodec.Encode(harness.ViewModel.Session.Document));
        Assert.False(harness.ViewModel.Session.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(SpriteResolutionStatus.MissingSheetFile, "missing")]
    [InlineData(SpriteResolutionStatus.SheetLoadFailed, "corrupt")]
    [InlineData(SpriteResolutionStatus.FrameOutsideSheet, "undersized")]
    public void TerrainPalette_MissingCorruptOrUndersizedLazySheetUsesPlaceholderAndDiagnosticWithoutDisablingTool(
        SpriteResolutionStatus status, string diagnostic)
    {
        using Harness harness = new(("Grass", 10));
        harness.Resolution = (_, reference) => Harness.Failure(status, reference, diagnostic);
        harness.ViewModel.ActiveTool = MapEditTool.Terrain;
        RecordingMapDrawTarget target = new();

        harness.Palette.RenderPalette(target);

        Assert.Contains(target.Rectangles, rectangle =>
            rectangle.Fill is SolidColorBrush brush && brush.Color == Color.FromArgb(0xFF, 0x00, 0xFF, 0xCC));
        Assert.Equal(2, target.Lines.Count);
        Assert.Contains(target.Texts, text => text.Text.Contains(diagnostic, StringComparison.Ordinal));
        Assert.True(harness.ViewModel.IsTerrainAvailable);
        Assert.Equal(MapEditTool.Terrain, harness.ViewModel.ActiveTool);
    }

    [AvaloniaFact]
    public void TerrainPalette_DrawsOnlyViewportEntriesInRuntimeOrder()
    {
        (string, int)[] definitions = Enumerable.Range(0, 100).Reverse().Select(i => ($"Set {i:000}", 1000 + i)).ToArray();
        using Harness harness = new(definitions);
        harness.Palette.Offset = TerrainPaletteControl.CellHeight * 50;

        harness.Palette.RenderPalette(new RecordingMapDrawTarget());

        Assert.Equal(2, harness.Resolved.Count);
        Assert.Equal(new[] { 1050, 1051 }, harness.Resolved.Select(reference => reference.Graphic));
    }

    [AvaloniaFact]
    public void TerrainPaletteControl_DisposeIsIdempotentUnbindsScrollbarAndIgnoresLaterCallbacks()
    {
        using Harness harness = new(("Grass", 10), ("Water", 20), ("Rock", 30));
        harness.Palette.RenderPalette(new RecordingMapDrawTarget());
        int resolutions = harness.Resolved.Count;
        double offset = harness.Palette.Offset;

        harness.Palette.Dispose();
        harness.Palette.Dispose();
        harness.Bar.Value = TerrainPaletteControl.CellHeight;
        harness.ViewModel.SelectedTerrainId = harness.Assets.Current.Terrain.Runtime!.EnabledSets[1].Id;
        TerrainReplacementPlan plan = harness.Assets.PrepareTerrainReplacement(
            harness.Assets.Current, TerrainTestData.Result(("Later", 40))).Plan!;
        harness.Assets.PublishTerrain(plan);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(offset, harness.Palette.Offset);
        Assert.Equal(resolutions, harness.Resolved.Count);
    }
}
