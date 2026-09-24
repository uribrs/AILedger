namespace AILedger.Core.Contracts;

// A mid-task consultation of the lesson store. A new event type rather than a mid-task
// LessonRecalled, so no history written before it existed can carry it (PALT1).
public sealed record LessonsConsulted(
    RunId RunId,
    LessonConsultationPurpose Purpose,
    string Question,
    // Trimmed, unique, ordinal-sorted.
    IReadOnlyList<string> Tags,
    // Unique, ordinal-sorted; may be empty except for Research.
    IReadOnlyList<ClaimId> ClaimIds,
    // InternalReconDocuments.ComputeClaimSetHash of the state before this event.
    string ClaimSetHash,
    // Unique, ordinal-sorted; may be empty. Empty is an honest outcome, not an error.
    IReadOnlyList<LessonId> ServedLessonIds,
    // Ordinal-sorted by Id; every id is also in ServedLessonIds.
    IReadOnlyList<Lesson> NewLessons) : LedgerEventData;
