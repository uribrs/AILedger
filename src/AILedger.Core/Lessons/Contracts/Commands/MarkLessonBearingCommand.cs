namespace AILedger.Core.Contracts;

public sealed record MarkLessonBearingCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    LessonSourceKind SourceKind,
    string SourceRecordId,
    LessonId? SupersedesLessonId = null,
    LessonClass? Class = null,
    string? Repo = null,
    IReadOnlyList<string>? Tags = null,
    string? Verify = null,
    string? DoNot = null,
    LessonActor? Actor = null,
    // Optional on the command because absent is a meaningful answer to both: a lesson about the
    // software is Domain, and a lesson everyone should read has no audience to narrow to.
    LessonKind? Kind = null,
    IReadOnlyList<RoleKind>? Audience = null,
    // Required at command time and nullable on the record. New marks must state the direction their
    // verify has to come out; the lessons already minted carry none and replay must keep reading
    // them. This is the twin-rule asymmetry: command-time policy may tighten, replay may not.
    VerifyExpectation? VerifyExpects = null) : LedgerCommand(ActorId, CausationId, CorrelationId);
