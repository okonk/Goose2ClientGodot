using System;
using System.Collections.Generic;
using MapEditor.App.Rendering;
using MapEditor.App.Terrain;
using MapEditor.Rendering;

namespace MapEditor.App.Tests.Fakes;

internal sealed class FakeTerrainCatalogPublisher : ITerrainCatalogPublisher
{
    public List<string> Events { get; } = new();
    public Action<string>? Sink;
    public Exception? PrepareSaveFailure;
    public Exception? PrepareLoadedFailure;
    public Exception? CommitSaveFailure;
    public Exception? CommitLoadedFailure;
    public TerrainOperationLease? LastSaveOperation;
    public AssetContext? LastSaveContext;
    public TerrainCatalogPreparedSave? LastPreparedSave;
    public TerrainOperationLease? LastLoadedOperation;
    public AssetContext? LastLoadedContext;
    public TerrainCatalogLoadResult? LastLoadedResult;
    public TerrainLoadedPublicationKind? LastLoadedKind;
    public TerrainCatalogSaveResult? LastCommittedSave;
    public int CommittedSaveCount;
    public int CommittedLoadedCount;
    public int DisposedSavePublications;
    public int DisposedLoadedPublications;

    public ITerrainCatalogSavePublication PrepareSave(TerrainOperationLease operation, AssetContext expectedContext, TerrainCatalogPreparedSave preparedSave)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(expectedContext);
        ArgumentNullException.ThrowIfNull(preparedSave);
        LastSaveOperation = operation;
        LastSaveContext = expectedContext;
        LastPreparedSave = preparedSave;
        if (PrepareSaveFailure is { } failure)
        {
            Record("PrepareSaveFailed");
            throw failure;
        }

        Record("PrepareSave");
        return new SavePublication(this, preparedSave.OperationId);
    }

    public ITerrainCatalogLoadedPublication PrepareLoaded(TerrainOperationLease operation, AssetContext expectedContext, TerrainCatalogLoadResult loadedReplacement, TerrainLoadedPublicationKind kind)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(expectedContext);
        ArgumentNullException.ThrowIfNull(loadedReplacement);
        if (loadedReplacement.IsValid != (kind == TerrainLoadedPublicationKind.ValidReload))
        {
            throw new InvalidOperationException($"Publication kind {kind} does not match the load result validity.");
        }

        LastLoadedOperation = operation;
        LastLoadedContext = expectedContext;
        LastLoadedResult = loadedReplacement;
        LastLoadedKind = kind;
        if (PrepareLoadedFailure is { } failure)
        {
            Record("PrepareLoadedFailed");
            throw failure;
        }

        Record("PrepareLoaded");
        return new LoadedPublication(this);
    }

    private void Record(string name)
    {
        Events.Add(name);
        Sink?.Invoke(name);
    }

    private sealed class SavePublication : ITerrainCatalogSavePublication
    {
        private readonly FakeTerrainCatalogPublisher _owner;
        private readonly string _operationId;
        private bool _finished;

        internal SavePublication(FakeTerrainCatalogPublisher owner, string operationId)
        {
            _owner = owner;
            _operationId = operationId;
        }

        public void Commit(TerrainCatalogSaveResult durableReplacement)
        {
            if (_finished)
            {
                throw new ObjectDisposedException(nameof(SavePublication));
            }

            if (durableReplacement.OperationId != _operationId)
            {
                throw new InvalidOperationException("The save result does not belong to this prepared save.");
            }

            if (_owner.CommitSaveFailure is { } failure)
            {
                _owner.Record("CommitSaveFailed");
                throw failure;
            }

            _finished = true;
            _owner.CommittedSaveCount++;
            _owner.LastCommittedSave = durableReplacement;
            _owner.Record("CommitSave");
        }

        public void Dispose()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _owner.DisposedSavePublications++;
            _owner.Record("DisposeSavePublication");
        }
    }

    private sealed class LoadedPublication : ITerrainCatalogLoadedPublication
    {
        private readonly FakeTerrainCatalogPublisher _owner;
        private bool _finished;

        internal LoadedPublication(FakeTerrainCatalogPublisher owner)
        {
            _owner = owner;
        }

        public void Commit()
        {
            if (_finished)
            {
                throw new ObjectDisposedException(nameof(LoadedPublication));
            }

            if (_owner.CommitLoadedFailure is { } failure)
            {
                _owner.Record("CommitLoadedFailed");
                throw failure;
            }

            _finished = true;
            _owner.CommittedLoadedCount++;
            _owner.Record("CommitLoaded");
        }

        public void Dispose()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _owner.DisposedLoadedPublications++;
            _owner.Record("DisposeLoadedPublication");
        }
    }
}
