namespace AILedger.Core.Contracts;

public sealed record LessonMark(
    LessonMarkId Id,
    LessonSourceKind SourceKind,
    string SourceRecordId,
    LessonId? SupersedesLessonId,
    Provenance Provenance,
    LessonClass? Class = null,
    string? Repo = null,
    IReadOnlyList<string>? Tags = null,
    string? Verify = null,
    string? DoNot = null,
    LessonActor? Actor = null,
    LessonKind? Kind = null,
    IReadOnlyList<RoleKind>? Audience = null,
    VerifyExpectation? VerifyExpects = null);
