using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using Xunit;

namespace MapEditor.App.Tests;

public class CrossMapPasteTests : IDisposable
{
    private readonly FakeEditorDialogs _dialogsA = new();
    private readonly FakeEditorDialogs _dialogsB = new();
    private readonly SharedTileClipboard _clipboard = new();
    private readonly MapDocumentViewModel _a;
    private readonly MapDocumentViewModel _b;

    public CrossMapPasteTests()
    {
        _a = new MapDocumentViewModel(new EditorDocumentController(_dialogsA, new MapFileStore()), _clipboard);
        _b = new MapDocumentViewModel(new EditorDocumentController(_dialogsB, new MapFileStore()), _clipboard);
    }

    public void Dispose()
    {
        _a.Dispose();
        _b.Dispose();
    }

    private Task Make3x3Async(MapDocumentViewModel viewModel, FakeEditorDialogs dialogs)
    {
        dialogs.NewMapResult = new NewMapRequest(3, 3);
        dialogs.DirtyResult = DirtyChoice.Discard;
        return viewModel.NewAsync();
    }

    [Fact]
    public async Task Paste_IntoOtherDocument_CopiesTiles()
    {
        await Make3x3Async(_b, _dialogsB);
        _a.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        _a.Session.Document.SetLayer(1, 0, 0, new MapTileLayer(2, 2));
        _a.Session.Document.SetLayer(0, 1, 0, new MapTileLayer(3, 3));
        _a.Session.Document.SetLayer(1, 1, 0, new MapTileLayer(4, 4));
        _a.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);
        _a.CopySelection();

        _b.BeginPasteMode();
        _b.ApplyPasteAt(0, 0);

        Assert.Equal(new MapTileLayer(1, 1), _b.Session.Document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(2, 2), _b.Session.Document[1, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(3, 3), _b.Session.Document[0, 1].GetLayer(0));
        Assert.Equal(new MapTileLayer(4, 4), _b.Session.Document[1, 1].GetLayer(0));
        Assert.True(_b.Session.IsDirty);
    }

    [Fact]
    public async Task Paste_IntoOtherDocument_LeavesSourceUnchanged()
    {
        await Make3x3Async(_b, _dialogsB);
        _a.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        _a.Session.Document.SetLayer(1, 0, 0, new MapTileLayer(2, 2));
        _a.Session.Document.SetLayer(0, 1, 0, new MapTileLayer(3, 3));
        _a.Session.Document.SetLayer(1, 1, 0, new MapTileLayer(4, 4));
        _a.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);
        _a.CopySelection();
        Assert.False(_a.Session.IsDirty);

        _b.BeginPasteMode();
        _b.ApplyPasteAt(0, 0);

        Assert.Equal(new MapTileLayer(1, 1), _a.Session.Document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(2, 2), _a.Session.Document[1, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(3, 3), _a.Session.Document[0, 1].GetLayer(0));
        Assert.Equal(new MapTileLayer(4, 4), _a.Session.Document[1, 1].GetLayer(0));
        Assert.False(_a.Session.IsDirty);
    }

    [Fact]
    public async Task Paste_IntoSmallerDocument_ClipsToDestination()
    {
        await Make3x3Async(_b, _dialogsB);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                _a.Session.Document.SetLayer(x, y, 0, new MapTileLayer(x + 1, y + 1));
            }
        }

        _a.SelectionRectangle = new MapTileRectangle(0, 0, 4, 4);
        _a.CopySelection();

        _b.BeginPasteMode();
        _b.ApplyPasteAt(2, 2);

        Assert.Equal(new MapTileLayer(1, 1), _b.Session.Document[2, 2].GetLayer(0));
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                if (x == 2 && y == 2)
                {
                    continue;
                }

                Assert.Equal(new MapTileLayer(0, 0), _b.Session.Document[x, y].GetLayer(0));
            }
        }
    }

    [Fact]
    public async Task Paste_WhollyOutsideDestination_LeavesDocumentClean()
    {
        await Make3x3Async(_b, _dialogsB);
        _a.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(9, 9));
        _a.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);
        _a.CopySelection();

        _b.BeginPasteMode();
        _b.ApplyPasteAt(10, 10);

        Assert.False(_b.Session.IsDirty);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Assert.Equal(new MapTileLayer(0, 0), _b.Session.Document[x, y].GetLayer(0));
            }
        }
    }

    [Fact]
    public async Task Paste_WithDifferentSelectedLayers_PairsTopDown()
    {
        await Make3x3Async(_b, _dialogsB);
        _a.SelectedLayers = 0b00011; // layers 0 and 1
        _a.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        _a.Session.Document.SetLayer(0, 0, 1, new MapTileLayer(2, 2));
        _a.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        _a.CopySelection();

        _b.SelectedLayers = 0b11000; // layers 3 and 4
        _b.BeginPasteMode();
        _b.ApplyPasteAt(0, 0);

        Assert.Equal(new MapTileLayer(2, 2), _b.Session.Document[0, 0].GetLayer(4));
        Assert.Equal(new MapTileLayer(1, 1), _b.Session.Document[0, 0].GetLayer(3));
        Assert.Equal(new MapTileLayer(0, 0), _b.Session.Document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(0, 0), _b.Session.Document[0, 0].GetLayer(1));
        Assert.Equal(new MapTileLayer(0, 0), _b.Session.Document[0, 0].GetLayer(2));
    }

    [Fact]
    public void Copy_InOneDocument_IsVisibleToTheOther()
    {
        _a.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);
        _a.CopySelection();

        Assert.NotNull(_a.Clipboard);
        Assert.Same(_a.Clipboard, _b.Clipboard);
    }

    [Fact]
    public void Dispose_StopsClipboardNotifications()
    {
        var raisedA = new List<string>();
        var raisedB = new List<string>();
        _a.PropertyChanged += (sender, e) => raisedA.Add(e.PropertyName ?? string.Empty);
        _b.PropertyChanged += (sender, e) => raisedB.Add(e.PropertyName ?? string.Empty);

        _a.Dispose();
        _b.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);
        _b.CopySelection();

        Assert.DoesNotContain(nameof(MapDocumentViewModel.Clipboard), raisedA);
        Assert.Contains(nameof(MapDocumentViewModel.Clipboard), raisedB);
    }
}
