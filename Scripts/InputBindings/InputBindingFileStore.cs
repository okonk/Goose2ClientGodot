using System;
using System.IO;
using System.Threading.Tasks;

namespace Goose2Client.InputBindings;

public sealed class InputBindingFileStore
{
    // Publishing replaces the destination, so it fails while a scanner, an editor, or a second
    // client holds input-bindings.json open without FILE_SHARE_DELETE. Those locks are transient.
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int Win32FacilityHResult = unchecked((int)0x80070000);

    private static readonly TimeSpan[] PublishRetryDelays =
    {
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(150),
        TimeSpan.FromMilliseconds(200),
    };

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

    public async Task<InputBindingFileStoreTransaction> SaveAsync(byte[] content)
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
            using (var stream = new FileStream(tempPath, FileMode.CreateNew))
            {
                stream.Write(content);
                stream.Flush(true);
            }

            await PublishWithRetryAsync(tempPath, hadExisting, backupPath).ConfigureAwait(false);
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

    private async Task PublishWithRetryAsync(string tempPath, bool hadExisting, string? backupPath)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (hadExisting)
                    File.Copy(_path, backupPath!, overwrite: true);

                FailBeforePublish?.Invoke(tempPath);

                File.Move(tempPath, _path, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < PublishRetryDelays.Length && IsTransientLock(ex))
            {
                await Task.Delay(PublishRetryDelays[attempt]).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransientLock(Exception ex)
    {
        if (ex is not IOException io || (io.HResult & Win32FacilityHResult) != Win32FacilityHResult)
            return false;

        return (io.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation;
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
