using System;
using System.Threading;
using System.Threading.Tasks;

namespace MapEditor.App.Terrain;

internal sealed class TerrainOperationLease : IDisposable
{
    private readonly TerrainOperationGate _gate;
    private bool _disposed;

    internal TerrainOperationLease(TerrainOperationGate gate)
    {
        _gate = gate;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Release(this);
    }
}

internal sealed class TerrainOperationGate : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private TerrainOperationLease? _current;
    private TaskCompletionSource? _completion;
    private int _active;
    private bool _disposed;

    public bool IsBusy
    {
        get
        {
            lock (_sync)
            {
                return _active > 0;
            }
        }
    }

    public Task Completion
    {
        get
        {
            lock (_sync)
            {
                return _completion?.Task ?? Task.CompletedTask;
            }
        }
    }

    public async Task<TerrainOperationLease> AcquireAsync()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        await _semaphore.WaitAsync();

        TerrainOperationLease lease;
        lock (_sync)
        {
            if (_disposed)
            {
                _semaphore.Release();
                throw new ObjectDisposedException(nameof(TerrainOperationGate));
            }

            lease = new TerrainOperationLease(this);
            _current = lease;
            if (++_active == 1)
            {
                _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        return lease;
    }

    internal void VerifyCurrent(TerrainOperationLease lease)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_current, lease))
            {
                throw new InvalidOperationException("The supplied lease is not this gate's active terrain operation lease.");
            }
        }
    }

    internal void Release(TerrainOperationLease lease)
    {
        TaskCompletionSource? completion;
        lock (_sync)
        {
            if (!ReferenceEquals(_current, lease))
            {
                return;
            }

            _current = null;
            if (--_active == 0)
            {
                completion = _completion;
                _completion = null;
            }
            else
            {
                completion = null;
            }
        }

        _semaphore.Release();
        completion?.TrySetResult();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }
    }
}
