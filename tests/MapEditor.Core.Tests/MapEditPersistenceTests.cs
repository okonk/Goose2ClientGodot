using System;
using System.IO;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class MapEditPersistenceTests
{
    private static string CreateTempDirectory()
    {
        return Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())).FullName;
    }

    private static MapDocument CreateDocument()
    {
        var document = MapDocument.Create(4, 3);
        document.SetFlags(0, 0, MapDocument.BlockedFlag);
        document.SetLayer(1, 2, 3, new MapTileLayer(7, 42));
        return document;
    }

    private static void PaintLayerCell(MapEditSession session, int x, int y)
    {
        session.SelectedTileLayer = new MapTileLayer(9, 8);
        session.BeginStroke(MapEditTool.Pencil, x, y);
        Assert.True(session.CompleteStroke());
    }

    [Fact]
    public void OpenedDocumentSession_StartsCleanWithEmptyHistory()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            store.Save(path, CreateDocument());
            var opened = store.Open(path);

            var session = new MapEditSession(opened.Document, initiallyDirty: false);

            Assert.False(session.IsDirty);
            Assert.False(session.CanUndo);
            Assert.False(session.CanRedo);
            Assert.Equal(0, session.RetainedHistoryUsedBytes);
            Assert.Equal(0, session.CurrentStateId);
            Assert.Equal(0, session.SavedStateId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NewDocumentSession_StartsDirtyWithEmptyHistory()
    {
        var directory = CreateTempDirectory();
        try
        {
            var session = new MapEditSession(MapDocument.Create(), initiallyDirty: true);

            Assert.True(session.IsDirty);
            Assert.Null(session.SavedStateId);
            Assert.False(session.CanUndo);
            Assert.False(session.CanRedo);
            Assert.Equal(0, session.RetainedHistoryUsedBytes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SuccessfulSave_DoesNotClearDirtyUntilMarkSaved()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            var revision = store.Save(path, CreateDocument());
            var opened = store.Open(path);
            var session = new MapEditSession(opened.Document, initiallyDirty: false);
            PaintLayerCell(session, 0, 0);
            Assert.True(session.IsDirty);

            store.Save(path, opened.Document, revision);

            Assert.True(session.IsDirty);
            Assert.True(session.CanUndo);
            Assert.Equal(224, session.RetainedHistoryUsedBytes);

            session.MarkSaved();

            Assert.False(session.IsDirty);
            Assert.True(session.CanUndo);
            Assert.Equal(224, session.RetainedHistoryUsedBytes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ValidationFailure_LeavesDirtyAndHistoryUnchanged()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            var revision = store.Save(path, CreateDocument());
            var opened = store.Open(path);
            var session = new MapEditSession(opened.Document, initiallyDirty: false);
            session.SelectedTileLayer = new MapTileLayer(short.MaxValue + 1, 0);
            session.BeginStroke(MapEditTool.Pencil, 0, 0);
            Assert.True(session.CompleteStroke());
            Assert.True(session.IsDirty);
            Assert.True(session.CanUndo);
            var usageBefore = session.RetainedHistoryUsedBytes;
            var tileBefore = opened.Document[0, 0].GetLayer(0);

            Assert.Throws<MapValidationException>(() => store.Save(path, opened.Document, revision));

            Assert.True(session.IsDirty);
            Assert.True(session.CanUndo);
            Assert.Equal(usageBefore, session.RetainedHistoryUsedBytes);
            Assert.Equal(tileBefore, opened.Document[0, 0].GetLayer(0));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExternalChangeFailure_LeavesDirtyAndHistoryUnchanged()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            var revision = store.Save(path, CreateDocument());
            var opened = store.Open(path);
            var session = new MapEditSession(opened.Document, initiallyDirty: false);
            PaintLayerCell(session, 0, 0);
            Assert.True(session.IsDirty);

            var external = CreateDocument();
            external.SetFlags(2, 1, MapDocument.BlockedFlag);
            File.WriteAllBytes(path, MapCodec.Encode(external));

            Assert.Throws<MapExternalChangeException>(() => store.Save(path, opened.Document, revision));

            Assert.True(session.IsDirty);
            Assert.Equal(0, session.SavedStateId);
            Assert.True(session.CanUndo);
            Assert.Equal(224, session.RetainedHistoryUsedBytes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NewSessionAfterSuccessfulOpenHasNoPriorSessionHistory()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new MapFileStore();
            var firstPath = Path.Combine(directory, "first.dat");
            store.Save(firstPath, CreateDocument());
            var firstOpened = store.Open(firstPath);
            var firstSession = new MapEditSession(firstOpened.Document);
            PaintLayerCell(firstSession, 0, 0);
            Assert.True(firstSession.IsDirty);
            Assert.Equal(224, firstSession.RetainedHistoryUsedBytes);

            var second = CreateDocument();
            second.SetFlags(3, 2, MapDocument.BlockedFlag);
            var secondPath = Path.Combine(directory, "second.dat");
            store.Save(secondPath, second);
            var secondOpened = store.Open(secondPath);
            var secondSession = new MapEditSession(secondOpened.Document);

            Assert.False(secondSession.IsDirty);
            Assert.False(secondSession.CanUndo);
            Assert.Equal(0, secondSession.RetainedHistoryUsedBytes);
            Assert.NotSame(firstOpened.Document, secondOpened.Document);

            secondSession.SelectedTileLayer = new MapTileLayer(2, 2);
            secondSession.BeginStroke(MapEditTool.Pencil, 1, 1);
            Assert.True(secondSession.CompleteStroke());

            Assert.Same(firstOpened.Document, firstSession.Document);
            Assert.True(firstSession.IsDirty);
            Assert.True(firstSession.CanUndo);
            Assert.Equal(224, firstSession.RetainedHistoryUsedBytes);
            Assert.Equal(new MapTileLayer(0, 0), secondOpened.Document[0, 0].GetLayer(0));
            Assert.Equal(new MapTileLayer(2, 2), secondOpened.Document[1, 1].GetLayer(0));
            Assert.True(secondSession.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FailedOpenCannotReplaceOrResetExistingSessionByCoreAPI()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            store.Save(path, CreateDocument());
            var opened = store.Open(path);
            var session = new MapEditSession(opened.Document);
            PaintLayerCell(session, 0, 0);
            Assert.True(session.IsDirty);
            Assert.True(session.CanUndo);
            Assert.Equal(224, session.RetainedHistoryUsedBytes);

            Assert.Throws<FileNotFoundException>(() => store.Open(Path.Combine(directory, "missing.dat")));
            var malformedPath = Path.Combine(directory, "malformed.dat");
            File.WriteAllBytes(malformedPath, new byte[] { 1, 2, 3 });
            Assert.Throws<MapFormatException>(() => store.Open(malformedPath));

            Assert.Same(opened.Document, session.Document);
            Assert.True(session.IsDirty);
            Assert.True(session.CanUndo);
            Assert.Equal(224, session.RetainedHistoryUsedBytes);
            Assert.Equal(0, session.SavedStateId);
            Assert.Equal(1, session.CurrentStateId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
