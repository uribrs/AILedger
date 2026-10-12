using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

public sealed class TaskMutationLockTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContentionHasDistinctTimeoutAndCanBeRetriedAfterRelease(bool fileLock)
    {
        using var root = new TemporaryDirectory();
        var path = Path.Combine(root.Path, "task.lock");
        var gate = new TaskMutationLock(TimeSpan.FromMilliseconds(100));
        var held = fileLock
            ? (IAsyncDisposable)new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
            : await gate.AcquireAsync(path, default);
        try
        {
            var error = await Record.ExceptionAsync(() => gate.AcquireAsync(path, default));
            Assert.True(error is TaskMutationLockTimeoutException,
                $"Expected contention, received {error?.GetType().Name}; inner HResult={error?.InnerException?.HResult:X8}");
        }
        finally { await held.DisposeAsync(); }
        await using var retried = await gate.AcquireAsync(path, default);
    }

    [Fact]
    public async Task UnavailableLockDirectoryIsNotReportedAsContention()
    {
        using var root = new TemporaryDirectory();
        var gate = new TaskMutationLock(TimeSpan.FromMilliseconds(100));
        var error = await Assert.ThrowsAsync<IOException>(() =>
            gate.AcquireAsync(Path.Combine(root.Path, "missing", "task.lock"), default));
        Assert.IsType<DirectoryNotFoundException>(error.InnerException);
    }
}
