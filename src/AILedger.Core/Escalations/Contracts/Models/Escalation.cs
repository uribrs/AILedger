namespace AILedger.Core.Contracts;

public sealed record Escalation(
    EscalationId Id,
    EscalationKind Kind,
    string Question,
    EscalationStatus Status,
    WorkItemId? WorkItemId,
    IReadOnlyList<string> Options,
    string? Recommendation,
    IReadOnlyList<EvidenceId> AttemptEvidenceIds,
    string? Resolution,
    ActorId? ResolvedBy,
    Provenance Provenance);
