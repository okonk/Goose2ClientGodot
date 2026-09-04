using System;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class MapEditSessionTests
{
    [Fact]
    public void Constructor_UsesDocumentLayerZeroEmptyBrushAndCleanBaseline()
    {
        var doc = MapDocument.Create(8, 8);
        var session = new MapEditSession(doc);

        Assert.Same(doc, session.Document);
        Assert.Equal((byte)1, session.SelectedLayers);
        Assert.Equal(0, session.TopLayer);
        Assert.Equal(new MapTileLayer(0, 0), session.SelectedTileLayer);
        Assert.False(session.HasActiveStroke);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.False(session.IsDirty);
        Assert.Equal(0, session.CurrentStateId);
        Assert.Equal(0, session.SavedStateId);
    }

    [Fact]
    public void Constructor_InitiallyDirtyHasNoSavedBaseline()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4), initiallyDirty: true);

        Assert.True(session.IsDirty);
        Assert.Null(session.SavedStateId);
        Assert.Equal(0, session.CurrentStateId);
    }

    [Fact]
    public void Constructor_RejectsNullDocument()
    {
        Assert.Throws<ArgumentNullException>(() => new MapEditSession(null!));
    }

    [Fact]
    public void SelectedLayers_DefaultsToLayer0Only()
    {
        var session = CreateSession();
        Assert.Equal((byte)1, session.SelectedLayers);
        Assert.Equal(0, session.TopLayer);
    }

    [Theory]
    [InlineData(0b00001, 0)]
    [InlineData(0b00101, 2)]
    [InlineData(0b11111, 4)]
    public void SelectedLayers_Mask_TopLayerIsHighestSetBit(byte mask, int expectedTop)
    {
        var session = CreateSession();
        session.SelectedLayers = mask;
        Assert.Equal(expectedTop, session.TopLayer);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)32)]
    [InlineData((byte)0b111111)]
    public void SelectedLayers_InvalidMask_ThrowsWithoutChangingSelection(byte mask)
    {
        var session = CreateSession();
        session.SelectedLayers = 0b00101;
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectedLayers = mask);
        Assert.Equal((byte)0b00101, session.SelectedLayers);
    }

    [Fact]
    public void Pencil_WithMultiLayerSelection_EditsOnlyTopmostLayer()
    {
        var session = CreateSession();
        session.SelectedLayers = 0b01001;
        session.SelectedTileLayer = new MapTileLayer(7, 42);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());

        Assert.Equal(new MapTileLayer(7, 42), session.Document[0, 0].GetLayer(3));
        Assert.Equal(new MapTileLayer(0, 0), session.Document[0, 0].GetLayer(0));
    }

    [Fact]
    public void Eyedropper_WithMultiLayerSelection_SamplesTopmostLayer()
    {
        var session = CreateSession();
        session.Document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        session.Document.SetLayer(0, 0, 3, new MapTileLayer(2, 2));
        session.SelectedLayers = 0b01001;

        session.BeginStroke(MapEditTool.Eyedropper, 0, 0);
        session.CompleteStroke();

        Assert.Equal(new MapTileLayer(2, 2), session.SelectedTileLayer);
    }

    [Fact]
    public void SelectionChanges_DoNotChangeDocumentHistoryOrDirtyState()
    {
        var doc = MapDocument.Create(4, 4);
        doc.SetLayer(1, 2, 3, new MapTileLayer(5, 6));
        var session = new MapEditSession(doc);

        session.SelectedLayers = 1 << 4;
        session.SelectedTileLayer = new MapTileLayer(-7, 8);

        Assert.Equal((byte)(1 << 4), session.SelectedLayers);
        Assert.Equal(4, session.TopLayer);
        Assert.Equal(new MapTileLayer(-7, 8), session.SelectedTileLayer);
        Assert.Equal(new MapTileLayer(5, 6), doc[1, 2].GetLayer(3));
        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal(0, session.History.RedoCount);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Pencil_ChangesOnlyCapturedLayerAtTarget()
    {
        var doc = MapDocument.Create(10, 10);
        doc.SetFlags(3, 4, 0b101);
        doc.SetLayer(3, 4, 0, new MapTileLayer(10, 11));
        doc.SetLayer(3, 4, 1, new MapTileLayer(20, 21));
        doc.SetLayer(3, 4, 2, new MapTileLayer(30, 31));
        doc.SetLayer(3, 4, 3, new MapTileLayer(40, 41));
        doc.SetLayer(3, 4, 4, new MapTileLayer(50, 51));
        doc.SetLayer(4, 4, 0, new MapTileLayer(70, 71));
        doc.SetLayer(4, 4, 3, new MapTileLayer(60, 61));
        var session = new MapEditSession(doc);
        session.SelectedLayers = 1 << 3;
        session.SelectedTileLayer = new MapTileLayer(-123456, 789);

        session.BeginStroke(MapEditTool.Pencil, 3, 4);
        Assert.True(session.CompleteStroke());

        var target = doc[3, 4];
        Assert.Equal(0b101, target.Flags);
        Assert.Equal(new MapTileLayer(10, 11), target.GetLayer(0));
        Assert.Equal(new MapTileLayer(20, 21), target.GetLayer(1));
        Assert.Equal(new MapTileLayer(30, 31), target.GetLayer(2));
        Assert.Equal(new MapTileLayer(-123456, 789), target.GetLayer(3));
        Assert.Equal(new MapTileLayer(50, 51), target.GetLayer(4));

        var neighbor = doc[4, 4];
        Assert.Equal(0, neighbor.Flags);
        Assert.Equal(new MapTileLayer(70, 71), neighbor.GetLayer(0));
        Assert.Equal(new MapTileLayer(60, 61), neighbor.GetLayer(3));
        for (var layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            if (layer != 0 && layer != 3)
            {
                Assert.Equal(new MapTileLayer(0, 0), neighbor.GetLayer(layer));
            }
        }
    }

    [Fact]
    public void Pencil_NoOpDoesNotCreateHistoryOrDirty()
    {
        var doc = MapDocument.Create(8, 8);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(0, 0);

        session.BeginStroke(MapEditTool.Pencil, 2, 2);
        Assert.False(session.CompleteStroke());

        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal(0, session.History.RedoCount);
        Assert.False(session.IsDirty);
        Assert.Equal(0, session.CurrentStateId);
    }

    [Fact]
    public void Eraser_ClearsOnlyCapturedLayerAtTarget()
    {
        var doc = MapDocument.Create(8, 8);
        doc.SetFlags(1, 1, 3);
        doc.SetLayer(1, 1, 0, new MapTileLayer(1, 1));
        doc.SetLayer(1, 1, 1, new MapTileLayer(2, 2));
        doc.SetLayer(1, 1, 2, new MapTileLayer(3, 3));
        doc.SetLayer(1, 1, 3, new MapTileLayer(4, 4));
        doc.SetLayer(1, 1, 4, new MapTileLayer(5, 5));
        var session = new MapEditSession(doc);
        session.SelectedLayers = 1 << 2;

        session.BeginStroke(MapEditTool.Eraser, 1, 1);
        Assert.True(session.CompleteStroke());

        var tile = doc[1, 1];
        Assert.Equal(3, tile.Flags);
        Assert.Equal(new MapTileLayer(1, 1), tile.GetLayer(0));
        Assert.Equal(new MapTileLayer(2, 2), tile.GetLayer(1));
        Assert.Equal(new MapTileLayer(0, 0), tile.GetLayer(2));
        Assert.Equal(new MapTileLayer(4, 4), tile.GetLayer(3));
        Assert.Equal(new MapTileLayer(5, 5), tile.GetLayer(4));
    }

    [Fact]
    public void Eraser_EmptyLayerIsNoOp()
    {
        var doc = MapDocument.Create(8, 8);
        var session = new MapEditSession(doc);
        session.SelectedLayers = 1 << 4;

        session.BeginStroke(MapEditTool.Eraser, 0, 0);
        Assert.False(session.CompleteStroke());

        Assert.Equal(0, session.History.UndoCount);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void BlockedToggle_XorsOnlyBlockedBitAndPreservesUnknownBits()
    {
        int before = int.MinValue | 1;
        var doc = MapDocument.Create(8, 8);
        doc.SetFlags(2, 3, before);
        doc.SetLayer(2, 3, 1, new MapTileLayer(9, 9));
        var session = new MapEditSession(doc);

        session.BeginStroke(MapEditTool.BlockedToggle, 2, 3);
        Assert.True(session.CompleteStroke());

        var tile = doc[2, 3];
        Assert.Equal(before ^ MapDocument.BlockedFlag, tile.Flags);
        Assert.Equal(new MapTileLayer(9, 9), tile.GetLayer(1));
        for (var layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            if (layer != 1)
            {
                Assert.Equal(new MapTileLayer(0, 0), tile.GetLayer(layer));
            }
        }
    }

    [Fact]
    public void Eyedropper_CopiesExactActiveLayerWithoutDocumentOrHistoryChange()
    {
        var doc = MapDocument.Create(8, 8);
        doc.SetFlags(2, 3, 1);
        doc.SetLayer(2, 3, 2, new MapTileLayer(33, 44));
        var session = new MapEditSession(doc);
        session.SelectedLayers = 1 << 2;
        session.SelectedTileLayer = new MapTileLayer(0, 0);

        session.BeginStroke(MapEditTool.Eyedropper, 2, 3);
        session.ContinueStroke(2, 3);

        Assert.Equal(new MapTileLayer(33, 44), session.SelectedTileLayer);
        Assert.True(session.HasActiveStroke);
        var tile = doc[2, 3];
        Assert.Equal(1, tile.Flags);
        Assert.Equal(new MapTileLayer(33, 44), tile.GetLayer(2));
        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal(0, session.History.RedoCount);
        Assert.False(session.IsDirty);
        Assert.False(session.CompleteStroke());
    }

    [Fact]
    public void Eyedropper_CancelRestoresPreviousBrush()
    {
        var doc = MapDocument.Create(8, 8);
        doc.SetLayer(0, 0, 1, new MapTileLayer(3, 4));
        var session = new MapEditSession(doc);
        session.SelectedLayers = 1 << 1;
        session.SelectedTileLayer = new MapTileLayer(7, 8);

        session.BeginStroke(MapEditTool.Eyedropper, 0, 0);
        Assert.Equal(new MapTileLayer(3, 4), session.SelectedTileLayer);

        session.CancelStroke();

        Assert.Equal(new MapTileLayer(7, 8), session.SelectedTileLayer);
        Assert.False(session.HasActiveStroke);
    }

    [Fact]
    public void InvalidToolOrCoordinate_ThrowsBeforeMutationOrActiveStroke()
    {
        var doc = MapDocument.Create(4, 4);
        var session = new MapEditSession(doc);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginStroke((MapEditTool)99, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginStroke(MapEditTool.Pencil, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginStroke(MapEditTool.Pencil, doc.Width, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginStroke(MapEditTool.Pencil, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginStroke(MapEditTool.Pencil, 0, doc.Height));
        Assert.False(session.HasActiveStroke);

        session.SelectedTileLayer = new MapTileLayer(4, 5);
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginStroke((MapEditTool)(-1), 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginStroke(MapEditTool.Pencil, 5, 0));
        Assert.True(session.HasActiveStroke);

        Assert.True(session.CompleteStroke());
        Assert.Equal(new MapTileLayer(4, 5), doc[1, 1].GetLayer(0));
        Assert.Equal(new MapTileLayer(0, 0), doc[0, 0].GetLayer(0));
    }

    [Fact]
    public void Stroke_CapturesLayerAndBrushAtBegin()
    {
        var doc = MapDocument.Create(8, 8);
        var session = new MapEditSession(doc);
        session.SelectedLayers = 1 << 0;
        session.SelectedTileLayer = new MapTileLayer(5, 6);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        session.SelectedLayers = 1 << 2;
        session.SelectedTileLayer = new MapTileLayer(9, 9);
        session.ContinueStroke(1, 0);
        Assert.True(session.CompleteStroke());

        for (var x = 0; x <= 1; x++)
        {
            var tile = doc[x, 0];
            Assert.Equal(0, tile.Flags);
            Assert.Equal(new MapTileLayer(5, 6), tile.GetLayer(0));
            for (var layer = 1; layer < MapDocument.LayerCount; layer++)
            {
                Assert.Equal(new MapTileLayer(0, 0), tile.GetLayer(layer));
            }
        }
    }

    [Fact]
    public void CompleteStroke_ReturnsTrueForDocumentChangeAndFalseForNoOpOrEyedropper()
    {
        var doc = MapDocument.Create(8, 8);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(2, 3);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());

        session.SelectedTileLayer = new MapTileLayer(0, 0);
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.False(session.CompleteStroke());

        doc.SetLayer(2, 2, 1, new MapTileLayer(4, 5));
        session.SelectedLayers = 1 << 1;
        session.BeginStroke(MapEditTool.Eyedropper, 2, 2);
        Assert.False(session.CompleteStroke());
    }

    [Fact]
    public void Undo_RestoresAllBeforeValuesAndRedoRestoresAllAfterValues()
    {
        var doc = MapDocument.Create(3, 3);
        doc.SetFlags(1, 0, 1);
        var session = new MapEditSession(doc);
        session.SelectedLayers = 1 << 1;
        session.SelectedTileLayer = new MapTileLayer(9, 9);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        session.ContinueStroke(2, 0);
        Assert.True(session.CompleteStroke());

        Assert.True(session.Undo());
        for (var x = 0; x < 3; x++)
        {
            var tile = doc[x, 0];
            Assert.Equal(x == 1 ? 1 : 0, tile.Flags);
            Assert.Equal(new MapTileLayer(0, 0), tile.GetLayer(1));
        }

        Assert.True(session.Redo());
        for (var x = 0; x < 3; x++)
        {
            var tile = doc[x, 0];
            Assert.Equal(x == 1 ? 1 : 0, tile.Flags);
            Assert.Equal(new MapTileLayer(9, 9), tile.GetLayer(1));
        }
    }

    [Fact]
    public void UndoAndRedo_ReturnFalseWhenUnavailable()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4));

        Assert.False(session.Undo());
        Assert.False(session.Redo());
    }

    [Fact]
    public void UndoThenEffectiveEdit_ClearsRedo()
    {
        var doc = MapDocument.Create(4, 4);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.True(session.CompleteStroke());
        Assert.True(session.Undo());
        Assert.True(session.CanRedo);

        session.BeginStroke(MapEditTool.Pencil, 2, 2);
        Assert.True(session.CompleteStroke());

        Assert.False(session.CanRedo);
        Assert.Equal(0, session.History.RedoCount);
        Assert.Equal(2, session.History.UndoCount);
    }

    [Fact]
    public void UndoThenNoOpOrEyedropper_PreservesRedo()
    {
        var doc = MapDocument.Create(4, 4);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.True(session.CompleteStroke());
        Assert.True(session.Undo());

        session.SelectedTileLayer = new MapTileLayer(0, 0);
        session.BeginStroke(MapEditTool.Pencil, 2, 2);
        Assert.False(session.CompleteStroke());
        Assert.True(session.CanRedo);

        doc.SetLayer(3, 3, 2, new MapTileLayer(2, 2));
        session.SelectedLayers = 1 << 2;
        session.BeginStroke(MapEditTool.Eyedropper, 3, 3);
        Assert.False(session.CompleteStroke());
        Assert.True(session.CanRedo);
        Assert.True(session.Redo());
    }

    [Fact]
    public void CompletedDrag_IsExactlyOneUndoStep()
    {
        var doc = MapDocument.Create(10, 10);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(3, 3);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        session.ContinueStroke(4, 0);
        session.ContinueStroke(4, 3);
        session.ContinueStroke(0, 3);
        Assert.True(session.CompleteStroke());
        Assert.Equal(1, session.History.UndoCount);

        Assert.True(session.Undo());
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                Assert.Equal(new MapTileLayer(0, 0), doc[x, y].GetLayer(0));
            }
        }
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public void InitiallyClean_EditUndoRedoTransitionsAroundBaseline()
    {
        var doc = MapDocument.Create(4, 4);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        Assert.False(session.IsDirty);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        Assert.True(session.IsDirty);

        Assert.True(session.Undo());
        Assert.False(session.IsDirty);

        Assert.True(session.Redo());
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void InitiallyDirty_EditUndoRemainsDirtyUntilMarkSaved()
    {
        var doc = MapDocument.Create(4, 4);
        var session = new MapEditSession(doc, initiallyDirty: true);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        Assert.True(session.IsDirty);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        Assert.True(session.IsDirty);

        Assert.True(session.Undo());
        Assert.True(session.IsDirty);

        session.MarkSaved();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void MarkSaved_ClearsDirtyWithoutClearingHistory()
    {
        var doc = MapDocument.Create(4, 4);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.True(session.CompleteStroke());
        Assert.True(session.IsDirty);

        session.MarkSaved();

        Assert.False(session.IsDirty);
        Assert.Equal(2, session.History.UndoCount);
        Assert.Equal(0, session.History.RedoCount);
    }

    [Fact]
    public void SaveAfterEdit_UndoIsDirtyAndRedoToSavepointIsClean()
    {
        var doc = MapDocument.Create(4, 4);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        session.MarkSaved();
        Assert.False(session.IsDirty);

        Assert.True(session.Undo());
        Assert.True(session.IsDirty);

        Assert.True(session.Redo());
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void BranchAfterUndo_CannotMatchDiscardedSavepointByStackPosition()
    {
        var doc = MapDocument.Create(4, 4);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.True(session.CompleteStroke());
        Assert.True(session.Undo());
        int discardedAfterId = session.History.PeekRedo()!.AfterStateId;

        session.BeginStroke(MapEditTool.Pencil, 2, 2);
        Assert.True(session.CompleteStroke());
        int branchAfterId = session.CurrentStateId;

        Assert.NotEqual(discardedAfterId, branchAfterId);
        Assert.True(session.Undo());
        Assert.NotEqual(discardedAfterId, session.CurrentStateId);
        Assert.True(session.Undo());
        Assert.NotEqual(discardedAfterId, session.CurrentStateId);
    }

    [Fact]
    public void CanceledAndNoOpStrokes_DoNotAdvanceDirtyStateIdentity()
    {
        var doc = MapDocument.Create(4, 4);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        int editId = session.CurrentStateId;
        session.MarkSaved();

        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        session.CancelStroke();
        Assert.Equal(editId, session.CurrentStateId);
        Assert.Equal(editId, session.SavedStateId);
        Assert.False(session.IsDirty);

        session.SelectedTileLayer = new MapTileLayer(0, 0);
        session.BeginStroke(MapEditTool.Pencil, 2, 2);
        Assert.False(session.CompleteStroke());
        Assert.Equal(editId, session.CurrentStateId);
        Assert.False(session.IsDirty);
    }

    private static MapEditSession CreateSession(int width = 4, int height = 4) => new(MapDocument.Create(width, height));
}
