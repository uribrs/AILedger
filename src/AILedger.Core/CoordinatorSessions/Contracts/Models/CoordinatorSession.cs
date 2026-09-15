namespace AILedger.Core.Contracts;

// One coordinating conversation, bracketed. A session has a start, an end, an actor and a harness
// identity, and it owns the runs it dispatched — which is the hierarchy the measurement needs:
// session, dispatched run, finding, disposition, next dispatch (D1).
//
// It is deliberately not an AgentRun. ALT1 records why: provider, model, provider session id and
// timeout are all meaningless here, and `cancelled` already carries four meanings on a run.
//
// Harness names the tool the coordinator is hosted in — `claude-code`, `codex-cli` — and
// HarnessSessionId is that tool's own identity for the conversation. The second is optional because
// a harness need not expose one, and it is the only thing a transcript can be identity-checked
// against: charging one session's usage to another is exactly the failure R4 names (D6, PD2).
public sealed record CoordinatorSession(
    CoordinatorSessionId Id,
    ActorId ActorId,
    string Harness,
    string? HarnessSessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);
