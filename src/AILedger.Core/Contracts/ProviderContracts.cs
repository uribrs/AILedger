namespace AILedger.Core.Contracts;

public enum AgentLaunchMode
{
    New,
    Resume
}

public enum PermissionProfile
{
    WorkspaceGoverned
}

public sealed record AgentLaunchRequest(
    RunId RunId,
    TaskId TaskId,
    ActorId ActorId,
    WorkItemId? WorkItemId,
    AgentLaunchMode Mode,
    string Provider,
    string ExecutablePath,
    string WorkingDirectory,
    // The agent has to reach the CLI to record truth, so it must be told where the ledger lives
    // and how to invoke the CLI from its own working directory, which is not the Ledger repository.
    string LedgerRoot,
    string LedgerCommandLine,
    string StandardInput,
    string? ProviderSessionId,
    PermissionProfile PermissionProfile,
    string? Model,
    string? OutputSchema,
    IReadOnlyList<string> AdditionalDirectories,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan Timeout);

public sealed record ProviderEvent(
    long Sequence,
    string Type,
    string RawJson,
    string? SessionId,
    bool IsTerminal,
    bool IsError,
    // When the launcher read this line. Sequence orders the stream and says nothing about how long
    // anything took, so a retained stream could show what an agent did and never when — the run's
    // own timing lived only in the provider's private store, and for codex that store is deleted with
    // the governed CODEX_HOME at run end (C32).
    //
    // Read by nothing that replays: ProviderEvent is not a ledger event and appears only inside the
    // sidecar, so this is additive with no compatibility surface. Nullable and trailing so every
    // sidecar already on disk still deserialises, and so the eighteen construction sites compile
    // untouched.
    DateTimeOffset? RecordedAt = null);

public sealed record AgentRunResult(
    RunId RunId,
    string Provider,
    string? ProviderSessionId,
    AgentRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int ExitCode,
    string? FinalOutput,
    IReadOnlyList<ProviderEvent> Events,
    string StandardError,
    string DetectedVersion,
    IReadOnlyList<string> EffectiveArguments,
    bool IsResume,
    string? Failure,
    // How many provider lines the drain had to cut to the per-line cap to keep reading. Carried out
    // of the adapter because a truncation nobody can see is a partial stream presented as whole, and
    // the launcher is the only thing positioned to put it on the run record (R1, D1).
    //
    // Null means no drain reported a count — a run that died before the process ran, or a runner
    // that does not count. Zero means the stream was watched and nothing was cut.
    int? TruncatedLines = null,
    // Whether the launch's own deadline is what ended this run. Carried out of the adapter for the
    // reason the truncation count is: the fact exists only at the moment of termination, and the
    // launcher is the only thing positioned to put it on the run record.
    //
    // It is an observation, not a deduction. IProcessRunner reports its own expired deadline as
    // ProviderProcessTimeoutException, so the adapter sets true only where a runner said so.
    // Everywhere else it is false, which is a statement and not a default: the adapter watched the
    // run end some other way. A launch whose adapter never returned at all produces no result to
    // read this from, and the run record's own field is null there (AgentRun.EndedAtTheLaunchTimeout).
    bool EndedAtTheLaunchTimeout = false);

public sealed record ProcessInvocation(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    string StandardInput,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan Timeout);

// Nullable and trailing so a process runner that counts nothing still constructs one. Null is
// "nobody counted"; zero is "counted, and no line was cut".
public sealed record ProcessExit(
    int ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int? TruncatedLines = null);

/// <summary>
/// A running count of the provider lines a drain has had to cut, readable while the drain is still
/// reading and after it has died.
/// </summary>
/// <remarks>
/// <see cref="ProcessExit.TruncatedLines"/> is the count of a run whose drains returned it, and it
/// is what a completed run records. A run that dies inside a drain has no exit to read — the
/// receiver's exception faults the drain task, so its return value is never produced — and that is
/// precisely the run whose count is worth the most: it says how much had already been cut when the
/// run was refused. Both drains increment one of these, so the caller that created it can read the
/// count on a path where nothing was returned to it (CC2, C3).
///
/// The count reads as null until the first cut. A failure that cut nothing keeps saying "nobody
/// counted" rather than claiming a clean stream it never finished watching.
/// </remarks>
public sealed class TruncatedLineTally
{
    private int _count;

    /// <summary>How many lines have been cut so far, or null if none has been.</summary>
    public int? Observed
    {
        get
        {
            var count = Volatile.Read(ref _count);
            return count == 0 ? null : count;
        }
    }

    /// <summary>Counts one cut line. Called from either drain, so the increment is atomic.</summary>
    public void Increment() => Interlocked.Increment(ref _count);
}

public interface IProcessRunner
{
    /// <summary>Runs one process to completion.</summary>
    /// <remarks>
    /// A runner that ends the process because <see cref="ProcessInvocation.Timeout"/> expired must
    /// report that by throwing <see cref="ProviderProcessTimeoutException"/>. It is the only thing
    /// in a position to know, and nothing downstream can recover the fact afterwards: the elapsed
    /// time cannot, because the run record's own interval is wider than the process's.
    /// </remarks>
    Task<ProcessExit> RunAsync(
        ProcessInvocation invocation,
        Func<string, CancellationToken, ValueTask> onStandardOutputLine,
        Func<string, CancellationToken, ValueTask> onStandardErrorLine,
        TruncatedLineTally? tally,
        CancellationToken cancellationToken);
}

/// <summary>
/// The runner's own deadline expired and it ended the provider process.
/// </summary>
/// <remarks>
/// This is the whole point of the type: it separates "the deadline this launch was given expired"
/// from "somebody cancelled", which a bare <see cref="OperationCanceledException"/> cannot. Measure
/// 11 used to close that gap by comparing the run's elapsed time against the limit, and the
/// comparison was unsound — the ledger run opens before the manifest is built and closes after the
/// result is persisted, so a provider that failed on its own terms just short of the limit was
/// reported as ended by the coordinator's limit (VC6, VE10).
///
/// It derives from <see cref="OperationCanceledException"/> so that a handler written before this
/// type existed still catches it and still behaves as it did.
/// </remarks>
public sealed class ProviderProcessTimeoutException(TimeSpan timeout)
    : OperationCanceledException($"Provider process exceeded its {timeout.TotalSeconds:0.###}-second limit.")
{
    /// <summary>The deadline that expired, as the invocation stated it.</summary>
    public TimeSpan Timeout { get; } = timeout;
}

public interface IAgentAdapter
{
    string Provider { get; }
    Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken);
    Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken);
}
