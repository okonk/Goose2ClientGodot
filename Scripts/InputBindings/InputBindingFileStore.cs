using System;
using System.IO;

namespace Goose2Client.InputBindings;

public sealed class InputBindingFileStore
{
    private readonly string _path;

    public InputBindingFileStore(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !System.IO.Path.IsPathRooted(absolutePath))
            throw new ArgumentException("Path must be an absolute path.", nameof(absolutePath));
        _path = System.IO.Path.GetFullPath(absolutePath);
    }

    public string Path => _path;

    public byte[]? Read()
    {
        FailBeforeRead?.Invoke();
        return File.Exists(_path) ? File.ReadAllBytes(_path) : null;
    }

    public InputBindingFileStoreTransaction Save(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var directory = System.IO.Path.GetDirectoryName(_path)!;
        var fileName = System.IO.Path.GetFileName(_path);
        var tempPath = System.IO.Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
        var hadExisting = File.Exists(_path);
        var backupPath = hadExisting
            ? System.IO.Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.bak")
            : null;

        try
        {
            using var stream = new FileStream(tempPath, FileMode.CreateNew);
            stream.Write(content);
            stream.Flush(true);

            if (hadExisting)
                File.Copy(_path, backupPath!, overwrite: true);

            FailBeforePublish?.Invoke(tempPath);

            File.Move(tempPath, _path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            if (hadExisting)
                TryDelete(backupPath!);
            throw;
        }

        return new InputBindingFileStoreTransaction(this, tempPath, backupPath, hadExisting);
    }

    // Fault injection seams for tests; null in production.
    public Action? FailBeforeRead { get; set; }
    public Action<string>? FailBeforePublish { get; set; }
    public Action<string>? FailDuringRollback { get; set; }
    public Action<string>? FailDuringCleanup { get; set; }

    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Orphaned temp/backup files are a cleanup concern, not a data-loss event.
        }
    }
}

public sealed class InputBindingFileStoreTransaction
{
    private readonly InputBindingFileStore _store;
    private readonly string _tempPath;
    private readonly string? _backupPath;
    private readonly bool _hadExisting;
    private bool _settled;

    internal InputBindingFileStoreTransaction(InputBindingFileStore store, string tempPath, string? backupPath, bool hadExisting)
    {
        _store = store;
        _tempPath = tempPath;
        _backupPath = backupPath;
        _hadExisting = hadExisting;
    }

    public bool Rollback()
    {
        if (_settled)
            return true;

        try
        {
            if (_hadExisting)
            {
                _store.FailDuringRollback?.Invoke(_backupPath!);
                File.Move(_backupPath!, _store.Path, overwrite: true);
            }
            else if (File.Exists(_store.Path))
            {
                _store.FailDuringRollback?.Invoke(_store.Path);
                File.Delete(_store.Path);
            }

            InputBindingFileStore.TryDelete(_tempPath);
            _settled = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Complete()
    {
        if (_settled)
            return true;

        var success = true;
        if (_backupPath is not null)
        {
            try
            {
                _store.FailDuringCleanup?.Invoke(_backupPath);
                File.Delete(_backupPath);
            }
            catch (FileNotFoundException)
            {
            }
            catch
            {
                success = false;
            }
        }

        InputBindingFileStore.TryDelete(_tempPath);

        if (success)
            _settled = true;

        return success;
    }
}
