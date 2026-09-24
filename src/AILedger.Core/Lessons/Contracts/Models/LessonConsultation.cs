namespace AILedger.Core.Contracts;

public sealed record LessonConsultation(
    EventId EventId,
    // Task version at which the event was applied (state.Version + 1). The arms compare it with
    // the episode markers on GovernedTaskState.
    long Version,
    ActorId ActorId,
    RunId RunId,
    LessonConsultationPurpose Purpose,
    string Question,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ClaimId> ClaimIds,
    string ClaimSetHash,
    IReadOnlyList<LessonId> ServedLessonIds,
    DateTimeOffset RecordedAt);
