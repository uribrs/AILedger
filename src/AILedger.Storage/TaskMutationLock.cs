using System.Collections.Concurrent;

namespace AILedger.Storage;

internal sealed class TaskMutationLock(TimeSpan? maximumWait = null)
{
    private readonly TimeSpan _maximumWait = maximumWait ?? TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessLocks =
        new(StringComparer.Ordinal);

    public async Task<IAsyncDisposable> AcquireAsync(string lockFilePath, CancellationToken cancellationToken)
    {
        var processLock = ProcessLocks.GetOrAdd(lockFilePath, static _ => new SemaphoreSlim(1, 1));
        var deadline = DateTimeOffset.UtcNow + _maximumWait;
        var acquired = await processLock.WaitAsync(_maximumWait, cancellationToken).ConfigureAwait(false);
        if (!acquired)
        {
            throw new TaskMutationLockTimeoutException(_maximumWait);
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

    private async Task<FileStream> AcquireFileLockAsync(
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
                    if (IsFileLockContention(exception))
                        throw new TaskMutationLockTimeoutException(_maximumWait, exception);
                    throw new IOException(
                        $"Could not acquire task mutation lock within {_maximumWait.TotalSeconds:0} seconds.",
                        exception);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsFileLockContention(IOException exception)
    {
        // Windows uses HRESULT_FROM_WIN32(ERROR_SHARING_VIOLATION). .NET on Unix
        // preserves the raw EWOULDBLOCK errno, whose value differs by platform.
        if (OperatingSystem.IsWindows()) return exception.HResult == unchecked((int)0x80070020);
        if (OperatingSystem.IsLinux()) return exception.HResult == 11;
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD()) return exception.HResult == 35;
        return false;
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
