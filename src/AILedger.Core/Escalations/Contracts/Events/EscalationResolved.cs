namespace AILedger.Core.Contracts;

public sealed record EscalationResolved(
    EscalationId EscalationId,
    EscalationStatus Status,
    string? Resolution,
    ActorId ResolvedBy) : LedgerEventData;
