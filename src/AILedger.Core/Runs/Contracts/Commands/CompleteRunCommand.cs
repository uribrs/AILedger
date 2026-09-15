namespace AILedger.Core.Contracts;

public sealed record CompleteRunCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    RunId RunId,
    AgentRunStatus Status,
    string? ProviderSessionId,
    // Plaintext, held only in the launching process. Never persisted.
    string? LaunchToken = null,
    // The brief this run was handed, known to the launcher only after the run started.
    string? ManifestHash = null,
    int? ManifestArtifactCount = null,
    // What the run cost. All but the first-write time are read off the provider's terminal event by
    // RunCostReader; that one the launcher measures. Every one of them is known only once the run
    // has ended, which is why they arrive here rather than on StartRunCommand.
    int? Turns = null,
    long? OutputTokens = null,
    long? MillisecondsToFirstLedgerWrite = null,
    // The model the provider actually served, when the launcher could read it. Absent leaves the
    // model recorded at run.started standing.
    string? Model = null,
    // The three input buckets, mapped per provider by RunCostReader and never summed into one
    // total: C7 measured them billed at roughly 1x, 1.25x and 0.1x (D3, D4, C6). 64-bit for the
    // reason RunCost gives: token counters are already in the millions and the largest runs are the
    // ones a 32-bit counter would drop (RC2).
    long? TokensInUncached = null,
    long? TokensInCacheWrite = null,
    long? TokensInCacheRead = null,
    // How many provider lines the drain cut to the per-line cap, read off the adapter's result by
    // the launcher. A hand-issued 'run complete' leaves it absent, which is correct: nobody was
    // watching that stream.
    int? TruncatedLines = null,
    // The limit this launch was given, and the provider's own reason for a run that ended other than
    // by completing. Both are the launcher's to record and both arrive here rather than on
    // StartRunCommand, because the launcher builds its request and reads its result after the kernel
    // has recorded the run. A hand-issued 'run complete' leaves both absent, which is correct: no
    // limit was given and no provider reported anything.
    //
    // The reason is relayed from the adapter and never composed from Status. C8 is the constraint and
    // AgentRun.TerminalFailureReason carries it: a run whose agent returned an adverse verdict is a
    // run that did its job, and a measure that read the status would count it as a failed dispatch.
    int? LaunchTimeoutSeconds = null,
    string? TerminalFailureReason = null,
    // Which condition ended the run, taken from AgentRunResult.EndedAtTheLaunchTimeout, which the
    // process runner sets by observing which cancellation source fired rather than by reading a
    // clock. A hand-issued 'run complete' leaves it absent, and so does a launch whose adapter never
    // returned: in both, nobody observed the termination, and measure 11 must leave the row unjudged
    // rather than infer it (VC4, VC6).
    bool? EndedAtTheLaunchTimeout = null) : LedgerCommand(ActorId, CausationId, CorrelationId);
