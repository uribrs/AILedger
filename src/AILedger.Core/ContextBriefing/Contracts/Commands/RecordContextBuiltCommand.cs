namespace AILedger.Core.Contracts;

// 'context build' is a read that conditionally records audit evidence. It is never gated, it
// cannot be refused for anything but an unknown task or an actor that may not build context, and a
// repeat of a brief already recorded appends nothing — but a new or changed skill set appends this
// event, so the command can move the task on and is not classified read-only in the CLI (SC2).
// The trace is the point: whether an actor was briefed — and with which skills, saying what — is a
// query against the events rather than an assumption.
//
// WorkItemId is what this one invocation was for. It stays on the event, which records an
// invocation truthfully; the ContextBuild projection drops it, because the projection is keyed on
// the actor and the skill set and would otherwise name whichever work item appended first (MD2).
public sealed record RecordContextBuiltCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    WorkItemId? WorkItemId,
    IReadOnlyList<ContextSkill> Skills) : LedgerCommand(ActorId, CausationId, CorrelationId);
