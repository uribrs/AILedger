namespace AILedger.Core.Contracts;

public sealed record ProposeDecisionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    DecisionId DecisionId,
    string Statement,
    string Rationale,
    IReadOnlyList<ClaimId> DependsOnClaims,
    DecisionId? Supersedes,
    LessonId? FromLesson = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record ResolveDecisionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    DecisionId DecisionId,
    DecisionStatus Status) : LedgerCommand(ActorId, CausationId, CorrelationId);
