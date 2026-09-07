using System;
using System.Collections.Generic;
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
    public void ApplyBlockedPatch_LeavesLayersUntouchedAndPreservesUnknownBits()
    {
        int before = int.MinValue | 1;
        var doc = MapDocument.Create(8, 8);
        doc.SetFlags(2, 3, before);
        doc.SetLayer(2, 3, 1, new MapTileLayer(9, 9));
        var session = new MapEditSession(doc);

        Assert.True(session.ApplyBlockedPatch(new MapTileRectangle(2, 3, 1, 1), blocked: true));

        var tile = doc[2, 3];
        Assert.Equal(before | MapDocument.BlockedFlag, tile.Flags);
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
    public void ApplyBlockedPatch_PreservesUnknownFlagBits()
    {
        var session = CreateSession(4, 4);
        int noisy = unchecked((int)0xFFFFFFFD);
        session.Document.SetFlags(1, 1, noisy);

        Assert.True(session.ApplyBlockedPatch(new MapTileRectangle(0, 0, 3, 3), blocked: true));
        Assert.Equal(noisy | MapDocument.BlockedFlag, session.Document[1, 1].Flags);

        Assert.True(session.ApplyBlockedPatch(new MapTileRectangle(0, 0, 3, 3), blocked: false));
        Assert.Equal(noisy & ~MapDocument.BlockedFlag, session.Document[1, 1].Flags);
    }

    [Fact]
    public void ApplyBlockedPatch_AlreadyInTargetState_IsNoOp()
    {
        var session = CreateSession(4, 4);
        Assert.False(session.ApplyBlockedPatch(new MapTileRectangle(0, 0, 4, 4), blocked: false));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void ApplyBlockedPatch_RectOutsideTheMap_Throws()
    {
        var session = CreateSession(4, 4);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.ApplyBlockedPatch(new MapTileRectangle(2, 2, 5, 5), blocked: true));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void BeginStroke_WithBlockedTool_Throws()
    {
        var session = CreateSession(4, 4);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.BeginStroke(MapEditTool.Blocked, 0, 0));
    }

    [Fact]
    public void Eyedropper_CopiesExactTopLayerWithoutDocumentOrHistoryChange()
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

    [Fact]
    public void FloodFill_EmptyRegion_FillsWholeMapAsOneUndoableCommand()
    {
        var session = CreateSession(5, 5);
        session.SelectedTileLayer = new MapTileLayer(3, 3);
        Assert.True(session.ApplyFloodFill(2, 2));
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                Assert.Equal(new MapTileLayer(3, 3), session.Document[x, y].GetLayer(0));
        Assert.True(session.CanUndo);
        Assert.True(session.Undo());
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                Assert.Equal(new MapTileLayer(0, 0), session.Document[x, y].GetLayer(0));
        Assert.True(session.CanRedo);
        Assert.True(session.Redo());
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                Assert.Equal(new MapTileLayer(3, 3), session.Document[x, y].GetLayer(0));
    }

    [Fact]
    public void FloodFill_SquareBoundary_FillsOnlyInterior()
    {
        var session = CreateSession(5, 5);
        for (int i = 1; i <= 3; i++)
        {
            session.Document.SetLayer(i, 1, 0, new MapTileLayer(1, 1));
            session.Document.SetLayer(i, 3, 0, new MapTileLayer(1, 1));
            session.Document.SetLayer(1, i, 0, new MapTileLayer(1, 1));
            session.Document.SetLayer(3, i, 0, new MapTileLayer(1, 1));
        }

        session.SelectedTileLayer = new MapTileLayer(9, 9);
        Assert.True(session.ApplyFloodFill(2, 2));

        Assert.Equal(new MapTileLayer(9, 9), session.Document[2, 2].GetLayer(0));
        Assert.Equal(new MapTileLayer(1, 1), session.Document[1, 1].GetLayer(0));
        Assert.Equal(new MapTileLayer(0, 0), session.Document[0, 0].GetLayer(0));
    }

    [Fact]
    public void FloodFill_DiagonalOnlyConnection_DoesNotFill()
    {
        var session = CreateSession(4, 4);
        // both in-map orthogonal neighbours of (0,0) are blockers; (1,1) and (2,2)
        // match the start value and are reachable only diagonally
        session.Document.SetLayer(1, 0, 0, new MapTileLayer(1, 1));
        session.Document.SetLayer(0, 1, 0, new MapTileLayer(1, 1));

        session.SelectedTileLayer = new MapTileLayer(9, 9);
        Assert.True(session.ApplyFloodFill(0, 0));

        Assert.Equal(new MapTileLayer(9, 9), session.Document[0, 0].GetLayer(0));
        // an 8-directional fill would reach (1,1) and then (2,2) through the diagonal
        Assert.Equal(new MapTileLayer(0, 0), session.Document[1, 1].GetLayer(0));
        Assert.Equal(new MapTileLayer(0, 0), session.Document[2, 2].GetLayer(0));
    }

    [Fact]
    public void FloodFill_DiagonalBlocker_DoesNotSealRegion()
    {
        var session = CreateSession(4, 4);
        session.Document.SetLayer(1, 1, 0, new MapTileLayer(1, 1));

        session.SelectedTileLayer = new MapTileLayer(3, 3);
        Assert.True(session.ApplyFloodFill(0, 0));

        // the blocker touches the start cell only diagonally, so the fill flows around it
        int filled = 0;
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                if (session.Document[x, y].GetLayer(0) == new MapTileLayer(3, 3))
                {
                    filled++;
                }
            }
        }

        Assert.Equal(15, filled);
    }

    [Fact]
    public void FloodFill_OneTileWideMap_FillsVertically()
    {
        // width == 1 degenerates the horizontal offsets into the vertical ones
        var session = CreateSession(1, 5);
        session.SelectedTileLayer = new MapTileLayer(3, 3);
        Assert.True(session.ApplyFloodFill(0, 2));
        for (int y = 0; y < 5; y++)
        {
            Assert.Equal(new MapTileLayer(3, 3), session.Document[0, y].GetLayer(0));
        }
    }

    [Fact]
    public void FloodFill_NonEmptyStartRegion_RefillsWithBrush()
    {
        var session = CreateSession(4, 4);
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                session.Document.SetLayer(x, y, 0, new MapTileLayer(3, 3));
            }
        }

        session.SelectedTileLayer = new MapTileLayer(9, 9);
        Assert.True(session.ApplyFloodFill(0, 0));

        Assert.Equal(new MapTileLayer(9, 9), session.Document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(9, 9), session.Document[3, 3].GetLayer(0));
    }

    [Fact]
    public void FloodFill_TargetEqualsStartValue_ReturnsFalseWithoutHistory()
    {
        var session = CreateSession(5, 5);
        session.SelectedTileLayer = new MapTileLayer(0, 0);
        Assert.False(session.ApplyFloodFill(0, 0));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void FloodFill_WithMultiLayerSelection_FillsOnlyTopmostLayer()
    {
        var session = CreateSession(5, 5);
        session.SelectedLayers = 0b01001;
        session.SelectedTileLayer = new MapTileLayer(3, 3);
        Assert.True(session.ApplyFloodFill(0, 0));
        Assert.Equal(new MapTileLayer(3, 3), session.Document[0, 0].GetLayer(3));
        Assert.Equal(new MapTileLayer(0, 0), session.Document[0, 0].GetLayer(0));
    }

    [Fact]
    public void LayerPatch_WritesCorrespondingLayersAsOneUndoableCommand()
    {
        var session = CreateSession(5, 5);
        MapTileLayer[]?[] patch = new MapTileLayer[MapDocument.LayerCount][];
        patch[1] = new MapTileLayer[] { new(5, 5), new(6, 6) };
        patch[3] = new MapTileLayer[] { new(7, 7), new(8, 8) };

        Assert.True(session.ApplyLayerPatch(1, 1, 2, 1, patch));
        Assert.Equal(new MapTileLayer(5, 5), session.Document[1, 1].GetLayer(1));
        Assert.Equal(new MapTileLayer(6, 6), session.Document[2, 1].GetLayer(1));
        Assert.Equal(new MapTileLayer(7, 7), session.Document[1, 1].GetLayer(3));
        Assert.Equal(new MapTileLayer(0, 0), session.Document[1, 1].GetLayer(0));
        Assert.Equal(new MapTileLayer(0, 0), session.Document[1, 1].GetLayer(2));

        Assert.True(session.Undo());
        Assert.Equal(new MapTileLayer(0, 0), session.Document[1, 1].GetLayer(1));
        Assert.Equal(new MapTileLayer(0, 0), session.Document[1, 1].GetLayer(3));
        Assert.True(session.Redo());
        Assert.Equal(new MapTileLayer(5, 5), session.Document[1, 1].GetLayer(1));
    }

    [Fact]
    public void LayerPatch_NoDifferingCells_ReturnsFalseWithoutHistory()
    {
        var session = CreateSession(5, 5);
        MapTileLayer[]?[] patch = new MapTileLayer[MapDocument.LayerCount][];
        patch[0] = new MapTileLayer[] { new(0, 0) };
        Assert.False(session.ApplyLayerPatch(0, 0, 1, 1, patch));
        Assert.False(session.CanUndo);
    }

    [Theory]
    [InlineData(4, 0, 2, 2)]
    [InlineData(0, 4, 2, 2)]
    [InlineData(0, 0, 0, 2)]
    public void LayerPatch_OutOfBounds_ThrowsWithoutMutation(int originX, int originY, int width, int height)
    {
        var session = CreateSession(5, 5);
        MapTileLayer[]?[] patch = new MapTileLayer[MapDocument.LayerCount][];
        patch[0] = new MapTileLayer[width * height];
        Assert.Throws<ArgumentOutOfRangeException>(() => session.ApplyLayerPatch(originX, originY, width, height, patch));
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void LayerPatch_WrongInnerArrayLength_ThrowsWithoutMutation()
    {
        var session = CreateSession(5, 5);
        session.Document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        MapTileLayer[]?[] patch = new MapTileLayer[MapDocument.LayerCount][];
        patch[0] = new MapTileLayer[3];
        Assert.Throws<ArgumentException>(() => session.ApplyLayerPatch(0, 0, 2, 2, patch));
        Assert.Equal(new MapTileLayer(1, 1), session.Document[0, 0].GetLayer(0));
        Assert.False(session.CanUndo);
    }

    [Theory]
    [InlineData(MapEditTool.Select)]
    [InlineData(MapEditTool.MultiSelect)]
    [InlineData(MapEditTool.FloodFill)]
    public void BeginStroke_NonStrokingTool_Throws(MapEditTool tool)
    {
        var session = CreateSession();
        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginStroke(tool, 0, 0));
    }

    [Fact]
    public void FloodFill_AndLayerPatch_WithActiveStroke_Throw()
    {
        var session = CreateSession(5, 5);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.Throws<InvalidOperationException>(() => session.ApplyFloodFill(1, 1));
        MapTileLayer[]?[] patch = new MapTileLayer[MapDocument.LayerCount][];
        patch[0] = new MapTileLayer[] { new(1, 1) };
        Assert.Throws<InvalidOperationException>(() => session.ApplyLayerPatch(0, 0, 1, 1, patch));
    }

    [Theory]
    [InlineData(0, 0, 3, 3, 5, 5, 0, 0, 3, 3)]
    [InlineData(3, 3, 4, 4, 5, 5, 3, 3, 2, 2)]
    [InlineData(-2, -2, 3, 3, 5, 5, 0, 0, 1, 1)]
    public void ClipTo_ClipsToBounds(int x, int y, int w, int h, int bw, int bh, int ex, int ey, int ew, int eh)
    {
        var clipped = new MapTileRectangle(x, y, w, h).ClipTo(bw, bh);
        Assert.Equal(new MapTileRectangle(ex, ey, ew, eh), clipped);
    }

    [Theory]
    [InlineData(5, 0, 2, 2, 5, 5)]
    [InlineData(0, 5, 2, 2, 5, 5)]
    [InlineData(-7, -7, 2, 2, 5, 5)]
    [InlineData(0, 0, 0, 3, 5, 5)]
    public void ClipTo_NoOverlapOrEmpty_ReturnsNull(int x, int y, int w, int h, int bw, int bh)
    {
        Assert.Null(new MapTileRectangle(x, y, w, h).ClipTo(bw, bh));
    }

    [Fact]
    public void ApplyResize_CropThenUndo_RestoresDiscardedTilesExactly()
    {
        var session = CreateSession(4, 4);
        session.Document.SetLayer(3, 3, 0, new MapTileLayer(7, 8));
        session.Document.SetLayer(2, 0, 0, new MapTileLayer(5, 5));
        session.Document.SetLayer(0, 2, 0, new MapTileLayer(6, 6));
        session.Document.SetFlags(3, 0, MapDocument.BlockedFlag);

        Assert.True(session.ApplyResize(new MapTileRectangle(0, 0, 2, 2)));
        Assert.Equal(2, session.Document.Width);

        Assert.True(session.Undo());
        Assert.Equal(4, session.Document.Width);
        Assert.Equal(new MapTileLayer(7, 8), session.Document[3, 3].GetLayer(0));
        Assert.Equal(new MapTileLayer(5, 5), session.Document[2, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(6, 6), session.Document[0, 2].GetLayer(0));
        Assert.True(session.Document[3, 0].IsBlocked);
    }

    [Fact]
    public void ApplyResize_GrowThenUndo_AllocatesNoSnapshotBuffer()
    {
        var session = CreateSession(4, 4);
        long before = session.RetainedHistoryUsedBytes;

        Assert.True(session.ApplyResize(new MapTileRectangle(0, 0, 8, 8)));

        Assert.Equal(MapEditCommand.BaseCommandBytes, session.RetainedHistoryUsedBytes - before);
        Assert.True(session.Undo());
        Assert.Equal(4, session.Document.Width);
    }

    [Fact]
    public void ApplyResize_IdentityWindow_IsNoOp()
    {
        var session = CreateSession(4, 4);
        Assert.False(session.ApplyResize(new MapTileRectangle(0, 0, 4, 4)));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void PaintResizePaint_UndoesBackThroughTheResizeInOrder()
    {
        var session = CreateSession(4, 4);
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        session.CompleteStroke();

        Assert.True(session.ApplyResize(new MapTileRectangle(0, 0, 6, 6)));

        session.SelectedTileLayer = new MapTileLayer(2, 2);
        session.BeginStroke(MapEditTool.Pencil, 5, 5);
        session.CompleteStroke();

        Assert.True(session.Undo());
        Assert.Equal(new MapTileLayer(0, 0), session.Document[5, 5].GetLayer(0));
        Assert.Equal(6, session.Document.Width);

        Assert.True(session.Undo());
        Assert.Equal(4, session.Document.Width);
        Assert.Equal(new MapTileLayer(1, 1), session.Document[0, 0].GetLayer(0));

        Assert.True(session.Undo());
        Assert.Equal(new MapTileLayer(0, 0), session.Document[0, 0].GetLayer(0));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void ApplyResize_WithActiveStroke_Throws()
    {
        var session = CreateSession(4, 4);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.Throws<InvalidOperationException>(
            () => session.ApplyResize(new MapTileRectangle(0, 0, 2, 2)));
    }

    [Fact]
    public void ApplyResize_CropUndoRedo_RestoresThenRecropsDimensionsAndContent()
    {
        var session = CreateSession(4, 4);
        session.Document.SetLayer(3, 3, 0, new MapTileLayer(7, 8));

        Assert.True(session.ApplyResize(new MapTileRectangle(0, 0, 2, 2)));
        Assert.Equal(2, session.Document.Width);
        Assert.Equal(2, session.Document.Height);

        Assert.True(session.Undo());
        Assert.Equal(4, session.Document.Width);
        Assert.Equal(4, session.Document.Height);
        Assert.Equal(new MapTileLayer(7, 8), session.Document[3, 3].GetLayer(0));

        Assert.True(session.Redo());
        Assert.Equal(2, session.Document.Width);
        Assert.Equal(2, session.Document.Height);
        Assert.Equal(new MapTileLayer(0, 0), session.Document[1, 1].GetLayer(0));
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void ApplyResize_EmptyBorderCrop_AllocatesNoSnapshotBuffer()
    {
        var session = CreateSession(4, 4);
        session.Document.SetLayer(0, 0, 0, new MapTileLayer(1, 2));
        long before = session.RetainedHistoryUsedBytes;

        Assert.True(session.ApplyResize(new MapTileRectangle(0, 0, 2, 2)));

        Assert.Equal(MapEditCommand.BaseCommandBytes, session.RetainedHistoryUsedBytes - before);
    }

    [Fact]
    public void ApplyResize_Rejected_LeavesDocumentHistoryStateIdAndDirtyUnchanged()
    {
        var session = CreateSession(4, 4);
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        session.CompleteStroke();
        session.MarkSaved();

        long usedBytes = session.RetainedHistoryUsedBytes;
        bool canUndo = session.CanUndo;
        bool canRedo = session.CanRedo;
        bool isDirty = session.IsDirty;
        int stateId = session.CurrentStateId;
        MapTileLayer painted = session.Document[0, 0].GetLayer(0);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.ApplyResize(new MapTileRectangle(0, 0, 1001, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.ApplyResize(new MapTileRectangle(int.MinValue, 0, 4, 4)));

        Assert.Equal(usedBytes, session.RetainedHistoryUsedBytes);
        Assert.Equal(canUndo, session.CanUndo);
        Assert.Equal(canRedo, session.CanRedo);
        Assert.Equal(isDirty, session.IsDirty);
        Assert.Equal(stateId, session.CurrentStateId);
        Assert.Equal(painted, session.Document[0, 0].GetLayer(0));
    }

    [Fact]
    public void Undo_OfAResize_RaisesResizedWithTheInverseOffset()
    {
        var session = CreateSession(4, 4);
        List<MapResizeTransform> raised = new();
        session.Resized += transform => raised.Add(transform);

        Assert.True(session.ApplyResize(new MapTileRectangle(-2, -1, 6, 5)));
        Assert.Equal(new MapResizeTransform(2, 1, 6, 5), raised[^1]);

        Assert.True(session.Undo());
        Assert.Equal(4, session.Document.Width);
        Assert.Equal(4, session.Document.Height);
        Assert.Equal(new MapResizeTransform(-2, -1, 4, 4), raised[^1]);
    }

    [Fact]
    public void ApplyResize_IdentityOrRejected_RaisesNoResizedEvent()
    {
        var session = CreateSession(4, 4);
        int raised = 0;
        session.Resized += _ => raised++;

        Assert.False(session.ApplyResize(new MapTileRectangle(0, 0, 4, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.ApplyResize(new MapTileRectangle(0, 0, 1001, 4)));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void DiscardRedo_RemovesRedoEntriesWithoutMutatingTilesOrSaveBaseline()
    {
        var session = CreateSession();
        int events = 0;
        session.HistoryChanged += () => events++;

        session.SelectedTileLayer = new MapTileLayer(1, 2);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        session.MarkSaved();
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.True(session.CompleteStroke());
        Assert.True(session.Undo());
        Assert.True(session.CanRedo);

        byte[] tiles = MapCodec.Encode(session.Document);
        long version = session.HistoryVersion;

        session.DiscardRedo();

        Assert.Equal(4, events);
        Assert.Equal(version + 1, session.HistoryVersion);
        Assert.False(session.CanRedo);
        Assert.True(session.CanUndo);
        Assert.Equal(tiles, MapCodec.Encode(session.Document));
        Assert.False(session.IsDirty);
        Assert.Equal(1, session.SavedStateId);

        session.DiscardRedo();
        Assert.Equal(version + 1, session.HistoryVersion);
        Assert.Equal(4, events);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void ClearHistory_EmptiesBothStacksWithoutMutatingTilesOrDirty()
    {
        var session = CreateSession();
        session.SelectedTileLayer = new MapTileLayer(1, 2);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        Assert.True(session.Undo());

        byte[] tiles = MapCodec.Encode(session.Document);
        bool dirty = session.IsDirty;
        long version = session.HistoryVersion;

        session.ClearHistory();

        Assert.Equal(version + 1, session.HistoryVersion);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(0, session.RetainedHistoryUsedBytes);
        Assert.Equal(tiles, MapCodec.Encode(session.Document));
        Assert.Equal(dirty, session.IsDirty);

        session.ClearHistory();
        Assert.Equal(version + 1, session.HistoryVersion);
    }

    private static MapEditSession CreateSession(int width = 4, int height = 4) => new(MapDocument.Create(width, height));
}
