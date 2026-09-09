using AILedger.Memory.Contracts;
using Microsoft.Data.Sqlite;
using System.IO;

namespace AILedger.Memory.Storage;

public sealed class SqliteMemoryProjectionStoreFactory : IMemoryProjectionStoreFactory
{
    private readonly string _databasePath;

    public SqliteMemoryProjectionStoreFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
    }

    public async Task<IMemoryProjectionStore> OpenCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_databasePath))
        {
            throw new FileNotFoundException(
                "The memory projection does not exist; run rebuild first.",
                _databasePath);
        }

        var store = new SqliteMemoryProjectionStore(_databasePath);
        try
        {
            await store.ValidateSchemaAsync(cancellationToken).ConfigureAwait(false);
            return store;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 11 or 26)
        {
            throw new InvalidDataException(
                "The memory projection is corrupt or is not a SQLite database; rebuild the disposable projection.",
                exception);
        }
    }

    public async Task<IMemoryProjectionRebuild> BeginRebuildAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(_databasePath)!;
        Directory.CreateDirectory(directory);
        var rebuildLease = AcquireRebuildLease(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_databasePath)}.{Guid.NewGuid():N}.rebuild");
        var store = new SqliteMemoryProjectionStore(temporaryPath);

        try
        {
            DeleteRebuildArtifacts(directory);
            await store.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return new SqliteMemoryProjectionRebuild(
                store,
                temporaryPath,
                _databasePath,
                rebuildLease);
        }
        catch
        {
            try
            {
                store.ClearPool();
                File.Delete(temporaryPath);
            }
            finally
            {
                rebuildLease.Dispose();
            }

            throw;
        }
    }

    public Task DropAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(_databasePath)!;
        if (!Directory.Exists(directory))
        {
            return Task.CompletedTask;
        }

        new SqliteMemoryProjectionStore(_databasePath).ClearPool();
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-shm");
        File.Delete(_databasePath + "-wal");
        return Task.CompletedTask;
    }

    private void DeleteRebuildArtifacts(string directory)
    {
        var prefix = $".{Path.GetFileName(_databasePath)}.";
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var fileName = Path.GetFileName(path);
            if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rebuildSuffix = fileName.IndexOf(".rebuild", prefix.Length, StringComparison.Ordinal);
            if (rebuildSuffix < 0 ||
                fileName[(rebuildSuffix + ".rebuild".Length)..] is not ("" or "-shm" or "-wal"))
            {
                continue;
            }

            File.Delete(path);
        }
    }

    private FileStream AcquireRebuildLease(string directory)
    {
        var leasePath = Path.Combine(
            directory,
            $".{Path.GetFileName(_databasePath)}.rebuild.lock");
        try
        {
            return new FileStream(
                leasePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                $"A rebuild of memory projection '{_databasePath}' is already in progress; wait for it to finish before rebuilding again.",
                exception);
        }
    }
}

internal sealed class SqliteMemoryProjectionRebuild : IMemoryProjectionRebuild
{
    private readonly SqliteMemoryProjectionStore _store;
    private readonly string _temporaryPath;
    private readonly string _currentPath;
    private readonly FileStream _rebuildLease;
    private bool _replaced;

    public SqliteMemoryProjectionRebuild(
        SqliteMemoryProjectionStore store,
        string temporaryPath,
        string currentPath,
        FileStream rebuildLease)
    {
        _store = store;
        _temporaryPath = temporaryPath;
        _currentPath = currentPath;
        _rebuildLease = rebuildLease;
    }

    public IMemoryProjectionStore Store => _store;

    public async Task ReplaceCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (_replaced)
        {
            throw new InvalidOperationException("The rebuilt projection has already been installed.");
        }

        await _store.VerifyIntegrityAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        _store.ClearPool();
        new SqliteMemoryProjectionStore(_currentPath).ClearPool();
        File.Move(_temporaryPath, _currentPath, overwrite: true);
        _replaced = true;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _store.ClearPool();
            if (!_replaced)
            {
                File.Delete(_temporaryPath);
                File.Delete(_temporaryPath + "-shm");
                File.Delete(_temporaryPath + "-wal");
            }
        }
        finally
        {
            _rebuildLease.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
