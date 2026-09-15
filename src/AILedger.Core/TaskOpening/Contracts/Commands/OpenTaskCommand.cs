namespace AILedger.Core.Contracts;

public sealed record OpenTaskCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    TaskId TaskId,
    string Title,
    string Goal,
    // Populated by the durable service from archived sibling tasks. Callers opening an isolated
    // aggregate can omit it and preserve the original command contract.
    IReadOnlyList<Lesson>? RecalledLessons = null,
    // The tags recall selects against, in the order given. Optional: a task opened without them
    // recalls exactly as it did before they existed.
    IReadOnlyList<string>? Tags = null) : LedgerCommand(ActorId, CausationId, CorrelationId);
