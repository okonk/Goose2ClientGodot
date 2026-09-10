using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MapEditor.App.Controls;
using MapEditor.App.Dialogs;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.Terrain;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Core.Terrain;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public sealed class TerrainSetsDialogTests
{
    [AvaloniaFact] public void Dialog_ShowsAllStatusesDiagnosticsProvenanceAndRegenerationWarning() { using Harness h = new(); TerrainSetsDialog d = h.Dialog(); Assert.NotEmpty((IEnumerable)d.TerrainMemberList.ItemsSource!); Assert.Contains("Converter regeneration overwrites", d.RegenerationWarningText.Text); Assert.NotNull(d.TerrainDiagnosticList.ItemsSource); }
    [AvaloniaFact] public void Dialog_SelectingTopologyShowsEveryRequiredVisualMask() { using Harness h = new(); TerrainSetsDialog d = h.Dialog(); d.TerrainTopologyCombo.SelectedItem = TerrainTopology.EightWay; Assert.Equal(TerrainMasks.Required(TerrainTopology.EightWay).Count, ((IEnumerable)d.TerrainMaskList.ItemsSource!).Cast<object>().Count()); }
    [AvaloniaFact] public void Dialog_AddRemoveAndMoveVariantUpdatesPreviewAndValidation() { using Harness h = new(); TerrainSetsDialog d = h.Dialog(); d.AddVariantSheetBox.Text = "1"; d.AddVariantGraphicBox.Text = "11"; d.AddVariantButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(2, ((IEnumerable)d.TerrainVariantList.ItemsSource!).Cast<object>().Count()); d.TerrainVariantList.SelectedIndex = 1; d.MoveVariantUpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); d.RemoveVariantButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Single(((IEnumerable)d.TerrainVariantList.ItemsSource!).Cast<object>()); }

    [AvaloniaFact]
    public void Dialog_InvalidEnableShowsExactFailuresAndStaysOpen()
    {
        using Harness h = new();
        string id = h.Manager.ViewModel.Sets[0].Id;
        TerrainSetsDialog d = h.Dialog(DirtyChoice.Cancel);
        d.Show();
        Dispatcher.UIThread.RunJobs();
        d.TerrainVariantList.SelectedIndex = 0;
        d.RemoveVariantButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        d.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.True(d.IsVisible);
        Assert.Equal(TerrainReviewStatus.Enabled, h.Manager.ViewModel.Sets[0].Status);
        Assert.Equal($"enabled-mask-empty: Enabled terrain '{id}' mask 0x00 has no variants.", d.ValidationSummary.Text);
        d.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact] public void Dialog_SaveFailureShowsErrorAndPreservesDraftForRetry() { using Harness h = new(); h.Manager.ViewModel.Rename(new(0), "Changed"); h.Operations.Fail = true; TerrainSetsDialog d = h.Dialog(); d.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Contains("could not be saved", d.ValidationSummary.Text); Assert.True(h.Manager.ViewModel.IsDirty); }

    [AvaloniaFact]
    public void Dialog_DirtyTitleCloseSaveDiscardCancelUsesSameSavePath()
    {
        VerifyDirtyClose(DirtyChoice.Save, closes: true, saves: true);
        VerifyDirtyClose(DirtyChoice.Discard, closes: true, saves: false);
        VerifyDirtyClose(DirtyChoice.Cancel, closes: false, saves: false);
    }

    [AvaloniaFact]
    public void Dialog_AllExactNamedControlsAreBoundAndScrollable()
    {
        using Harness h = new();
        TerrainSetsDialog d = h.Dialog();
        d.Show();
        Dispatcher.UIThread.RunJobs();
        string[] names = { "TerrainSetList", "TerrainNameBox", "TerrainStatusCombo", "TerrainTopologyCombo", "TerrainMemberList", "TerrainDiagnosticList", "TerrainMaskList", "TerrainVariantList", "AddVariantSheetBox", "AddVariantGraphicBox", "AddVariantButton", "RemoveVariantButton", "MoveVariantUpButton", "MoveVariantDownButton", "RemoveOrphanMaskButton", "RemoveAllOrphansButton", "RegenerateIdButton", "TerrainMaskPreview", "PreviewDiagnosticText", "ValidationSummary", "RegenerationWarningText", "SaveButton", "CancelButton" };
        Assert.All(names, name => Assert.NotNull(d.FindControl<Control>(name)));

        Assert.Same(h.Manager.ViewModel.Sets, d.TerrainSetList.ItemsSource);
        Assert.Same(h.Manager.ViewModel.Sets[0], d.TerrainSetList.SelectedItem);
        Assert.Equal(h.Manager.ViewModel.Sets[0].DisplayName, d.TerrainNameBox.Text);
        Assert.Equal(h.Manager.ViewModel.Sets[0].Status, d.TerrainStatusCombo.SelectedItem);
        Assert.Equal(h.Manager.ViewModel.Sets[0].Topology, d.TerrainTopologyCombo.SelectedItem);
        Assert.Same(h.Manager.ViewModel.Sets[0].Members, d.TerrainMemberList.ItemsSource);
        Assert.NotNull(d.TerrainDiagnosticList.ItemsSource);
        Assert.NotNull(d.TerrainMaskList.ItemsSource);
        Assert.Same(((IEnumerable)d.TerrainMaskList.ItemsSource!).Cast<TerrainMaskDraft>().First().Variants, d.TerrainVariantList.ItemsSource);
        Assert.Equal(h.Manager.ViewModel.IsDirty, d.SaveButton.IsEnabled);

        Assert.Contains(d.TerrainSetList.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == h.Manager.ViewModel.Sets[0].DisplayName);
        Assert.DoesNotContain(d.TerrainSetList.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("TerrainSetDraft {") == true);
        const string diagnosticMessage = "Readable diagnostic message";
        d.TerrainDiagnosticList.ItemsSource = new[] { new TerrainDiagnostic("test", diagnosticMessage) };
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(d.TerrainDiagnosticList.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == diagnosticMessage);
        Assert.DoesNotContain(d.TerrainDiagnosticList.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("TerrainDiagnostic {") == true);

        ListBox[] lists = { d.TerrainSetList, d.TerrainMemberList, d.TerrainDiagnosticList, d.TerrainMaskList, d.TerrainVariantList };
        Assert.All(lists, list =>
        {
            Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(list));
            Assert.NotEmpty(list.GetVisualDescendants().OfType<ScrollViewer>());
        });
        Assert.NotNull(d.TerrainNameBox.FindAncestorOfType<ScrollViewer>());
        d.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Dialog_LazyMissingOrCorruptPreviewUsesPlaceholderAndDiagnostic()
    {
        using Harness h = new();
        h.Manager.ViewModel.RemoveVariant(new(0), 0, 0);
        h.Manager.ViewModel.AddVariant(new(0), 0, new TerrainGraphicReference(1, 999));
        TerrainSetsDialog d = h.Dialog();
        d.TerrainMaskPreview.Arrange(new Rect(0, 0, 80, 40));
        var target = new RecordingMapDrawTarget();
        string expected = h.Controller.Current.Resolve(new SpriteReference(1, 999)).Diagnostic!;

        d.TerrainMaskPreview.RenderPreview(target);

        RecordingMapDrawTarget.RectangleDraw placeholder = Assert.Single(target.Rectangles);
        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0xFF, 0xCC), Assert.IsType<SolidColorBrush>(placeholder.Fill).Color);
        Assert.Equal(2, target.Lines.Count);
        Assert.Empty(target.Images);
        Assert.Equal(expected, d.TerrainMaskPreview.Diagnostic);
        Assert.Equal(expected, d.PreviewDiagnosticText.Text);
    }

    private static void VerifyDirtyClose(DirtyChoice choice, bool closes, bool saves)
    {
        using Harness h = new();
        AssetContext contextBefore = h.Controller.Current;
        object? runtimeBefore = contextBefore.Terrain.Runtime;
        byte[] fileBefore = File.ReadAllBytes(h.CatalogPath);
        h.Manager.ViewModel.Rename(new(0), "Changed");
        TerrainSetsDialog d = h.Dialog(choice);
        d.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Terrain Sets *", d.Title);

        d.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(!closes, d.IsVisible);
        Assert.Equal(saves ? 1 : 0, h.Operations.Creates);
        if (saves)
        {
            Assert.NotEqual(fileBefore, File.ReadAllBytes(h.CatalogPath));
            Assert.Equal(TerrainCatalogJson.Serialize(h.Controller.Current.Terrain.Source!), File.ReadAllText(h.CatalogPath));
            Assert.False(h.Manager.ViewModel.IsDirty);
        }
        else
        {
            Assert.Equal(fileBefore, File.ReadAllBytes(h.CatalogPath));
            Assert.Same(contextBefore, h.Controller.Current);
            Assert.Same(runtimeBefore, h.Controller.Current.Terrain.Runtime);
            Assert.True(h.Manager.ViewModel.IsDirty);
        }
        if (d.IsVisible)
        {
            h.Manager.ViewModel.AcceptChanges();
            d.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class Harness : IDisposable
    {
        internal readonly AssetFixture Fixture = new();
        internal readonly FakeEditorDialogs Dialogs = new();
        internal readonly WorkspaceViewModel Workspace;
        internal readonly AssetContextController Controller;
        internal readonly Ops Operations = new();
        internal readonly TerrainCatalogManager Manager;
        internal string CatalogPath => Path.Combine(Fixture.AssetDirectory, "terrain-brushes.json");
        internal Harness()
        {
            Fixture.WriteManifest("{\"tileSize\":32,\"sheets\":{\"1\":{\"10\":[0,0,32,32],\"11\":[32,0,32,32]}}}");
            Fixture.WriteSheet(1, 64, 32);
            Fixture.WriteTerrainCatalog(TerrainTestData.Catalog(("A",10)));
            Workspace = new WorkspaceViewModel(Dialogs, new MapFileStore());
            Controller = new AssetContextController(Workspace, new AppSettingsStore(Path.Combine(Fixture.Root,"settings.json")));
            Assert.True(Controller.TryOpen(Fixture.AssetDirectory));
            Manager = new TerrainCatalogManager(Controller, Controller.Current, new TerrainCatalogFileStore(Operations));
        }
        internal TerrainSetsDialog Dialog(DirtyChoice choice = DirtyChoice.Cancel) => new(Manager, () => Task.FromResult(choice));
        public void Dispose() { Controller.Dispose(); Fixture.Dispose(); }
    }

    private sealed class Ops : ITerrainCatalogFileOperations
    {
        internal bool Fail;
        internal int Creates;
        private MemoryStream? stream;
        public bool Exists(string path) => true;
        public Stream CreateFile(string path) { Creates++; if (Fail) throw new IOException("failure"); return stream = new MemoryStream(); }
        public void FlushToDisk(Stream stream) { }
        public void Replace(string sourcePath, string destinationPath) => File.WriteAllBytes(destinationPath, stream!.ToArray());
        public void Move(string sourcePath, string destinationPath) => File.WriteAllBytes(destinationPath, stream!.ToArray());
        public void Delete(string path) { }
    }
}
