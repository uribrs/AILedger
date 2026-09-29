namespace AILedger.Storage.Findings;

// Test-only constructor seam at the actual write/flush boundary, never a second event store.
internal sealed record FindingsStorageFaults(
    Action? BeforeWrite = null,
    Func<FileStream, ReadOnlyMemory<byte>, CancellationToken, Task>? Write = null,
    Action<FileStream>? Flush = null);
