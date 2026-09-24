namespace AILedger.Core.Contracts;

// A mid-task consultation of the lesson store. A new event type rather than a mid-task
// LessonRecalled, so no history written before it existed can carry it (PALT1).
// The command producer emits trimmed tags and ordinal-sorted lists. Replay checks integrity,
// including uniqueness, without imposing those formatting conventions on historical events.
public sealed record LessonsConsulted(
    RunId RunId,
    LessonConsultationPurpose Purpose,
    string Question,
    // Nonblank and unique.
    IReadOnlyList<string> Tags,
    // Unique; the command requires at least one for Research.
    IReadOnlyList<ClaimId> ClaimIds,
    // InternalReconDocuments.ComputeClaimSetHash of the state before this event.
    string ClaimSetHash,
    // Unique; may be empty. Empty is an honest outcome, not an error.
    IReadOnlyList<LessonId> ServedLessonIds,
    // Every id is also in ServedLessonIds.
    IReadOnlyList<Lesson> NewLessons) : LedgerEventData;
