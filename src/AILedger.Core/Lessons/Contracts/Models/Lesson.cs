namespace AILedger.Core.Contracts;

public sealed record Lesson(
    LessonId Id,
    TaskId SourceTaskId,
    LessonSourceKind SourceKind,
    string SourceRecordId,
    string Statement,
    string Outcome,
    IReadOnlyList<string> Citations,
    Provenance Provenance,
    LessonId? SupersedesLessonId = null,
    // Nullable only so histories written before lesson classification can still replay. New marks
    // require every metadata field at command time and therefore mint populated lessons.
    LessonClass? Class = null,
    string? Repo = null,
    IReadOnlyList<string>? Tags = null,
    // The three fields that make a recalled row actionable rather than prose: a command that
    // re-establishes the belief today, the prohibition it carries, and who established it.
    string? Verify = null,
    string? DoNot = null,
    LessonActor? Actor = null,
    // What the lesson is about, and not to be read as SourceKind: that names the record this was
    // minted from. Absent reads as Domain, which is what all 166 lessons minted before it are.
    LessonKind? Kind = null,
    // The roles this lesson is addressed to, checked against RoleKind rather than LessonActor
    // because the address is who needs to read it and LessonActor is who established it. Absent
    // reaches every role, which is what every lesson minted before this field existed means.
    IReadOnlyList<RoleKind>? Audience = null,
    // Which way Verify has to come out. Absent means the row records no direction and so cannot be
    // rechecked, which is the state every lesson minted before this field existed is in.
    VerifyExpectation? VerifyExpects = null);
