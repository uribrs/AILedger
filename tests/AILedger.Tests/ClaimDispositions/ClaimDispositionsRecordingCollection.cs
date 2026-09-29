namespace AILedger.Tests.ClaimDispositions;

// These real disk/relay fixtures add substantial flush traffic. Keep their deadline-sensitive
// best-effort telemetry assertions independent of unrelated suites' I/O. Explicit concurrency
// tests still run their writers and retries concurrently within this collection.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ClaimDispositionsRecordingCollection
{
    public const string Name = "ClaimDispositions recording I/O";
}
