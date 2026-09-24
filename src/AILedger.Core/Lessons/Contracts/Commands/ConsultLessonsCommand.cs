namespace AILedger.Core.Contracts;

public sealed record ConsultLessonsCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    RunId RunId,
    LessonConsultationPurpose Purpose,
    string Question,
    IReadOnlyList<string> Tags,
    // The claims this consultation is about. Required (at least one) for Research; optional otherwise.
    IReadOnlyList<ClaimId> ClaimIds,
    // Injected by the Storage service before Core handles the command, exactly as
    // OpenTaskCommand.RecalledLessons is. Null when no service injected anything.
    IReadOnlyList<Lesson>? Candidates = null) : LedgerCommand(ActorId, CausationId, CorrelationId);
