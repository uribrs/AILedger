namespace AILedger.Core.Contracts;

public sealed record CompleteWorkItemCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    WorkItemId WorkItemId,
    // Why this item is being completed with no verifier run behind it. Only an operator may pass
    // it, and passing it puts the waiver in the log rather than leaving it unrecorded.
    string? WithoutVerificationReason = null) : LedgerCommand(ActorId, CausationId, CorrelationId);
