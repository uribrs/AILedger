namespace AILedger.Tests.Alternatives;

// These real disk/relay fixtures add substantial flush traffic. Keep their deadline-sensitive
// best-effort telemetry assertions independent of unrelated suites' I/O. Explicit concurrency
// tests still run their writers and retries concurrently within this collection.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AlternativesRecordingCollection
{
    public const string Name = "Alternatives recording I/O";
}
