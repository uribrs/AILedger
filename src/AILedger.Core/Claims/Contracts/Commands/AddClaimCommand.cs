namespace AILedger.Core.Contracts;

public sealed record AddClaimCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ClaimId ClaimId,
    string Statement,
    string? ConsequenceIfWrong,
    LessonId? FromLesson = null) : LedgerCommand(ActorId, CausationId, CorrelationId);
