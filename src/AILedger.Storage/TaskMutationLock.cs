using System.Collections.Concurrent;

namespace AILedger.Storage;

internal sealed class TaskMutationLock
{
    private static readonly TimeSpan MaximumFileLockWait = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessLocks =
        new(StringComparer.Ordinal);

    public async Task<IAsyncDisposable> AcquireAsync(string lockFilePath, CancellationToken cancellationToken)
    {
        var processLock = ProcessLocks.GetOrAdd(lockFilePath, static _ => new SemaphoreSlim(1, 1));
        var deadline = DateTimeOffset.UtcNow + MaximumFileLockWait;
        var acquired = await processLock.WaitAsync(MaximumFileLockWait, cancellationToken).ConfigureAwait(false);
        if (!acquired)
        {
            throw new IOException(
                $"Could not acquire task mutation lock within {MaximumFileLockWait.TotalSeconds:0} seconds.");
        }

        try
        {
            var fileLock = await AcquireFileLockAsync(lockFilePath, deadline, cancellationToken).ConfigureAwait(false);
            return new Lease(processLock, fileLock);
        }
        catch
        {
            processLock.Release();
            throw;
        }
    }

    private static async Task<FileStream> AcquireFileLockAsync(
        string lockFilePath,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException exception)
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    throw new IOException(
                        $"Could not acquire task mutation lock within {MaximumFileLockWait.TotalSeconds:0} seconds.",
                        exception);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class Lease(SemaphoreSlim processLock, FileStream fileLock) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await fileLock.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                processLock.Release();
            }
        }
    }
}
