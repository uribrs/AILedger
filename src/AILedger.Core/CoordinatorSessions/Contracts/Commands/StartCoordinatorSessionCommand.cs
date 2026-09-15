namespace AILedger.Core.Contracts;

// Opens the bracket a coordinator puts around its own work. The session records no cost, volume,
// or self-assessment because every measure over it is derived from the log.
public sealed record StartCoordinatorSessionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    CoordinatorSessionId SessionId,
    // Which harness hosts the coordinator — `claude-code`, `codex-cli`. Required: a session that
    // does not say what it ran in cannot have its usage record looked for, and "no record" and
    // "never asked" are different absences (D6).
    string Harness,
    // The harness's own identity for this conversation, when it exposes one. It is what a transcript
    // is identity-checked against, and a session carrying none reports the usage read as an absence
    // rather than trusting a path (R4, PD2).
    string? HarnessSessionId = null) : LedgerCommand(ActorId, CausationId, CorrelationId);
