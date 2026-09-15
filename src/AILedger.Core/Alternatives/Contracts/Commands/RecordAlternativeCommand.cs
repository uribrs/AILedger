namespace AILedger.Core.Contracts;

public sealed record RecordAlternativeCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    AlternativeId AlternativeId,
    string Statement,
    string RejectionRationale,
    DecisionId? ReplacedByDecisionId,
    LessonId? FromLesson = null) : LedgerCommand(ActorId, CausationId, CorrelationId);
