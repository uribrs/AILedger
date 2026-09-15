namespace AILedger.Core.Contracts;

public sealed record RaiseEscalationCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    EscalationId EscalationId,
    EscalationKind Kind,
    string Question,
    WorkItemId? WorkItemId,
    IReadOnlyList<string> Options,
    string? Recommendation,
    IReadOnlyList<EvidenceId> AttemptEvidenceIds) : LedgerCommand(ActorId, CausationId, CorrelationId);
