using AILedger.Core.Contracts;

namespace AILedger.Core.Application;

/// <summary>
/// What a lesson's optional fields mean when they are absent, for the readers that have to act on
/// them. Nullable is a persistence fact: the lessons minted before a field existed carry none of it
/// and replay must keep reading them. It is not the meaning. A reader that treats null as "nothing
/// to say" gives the field no default at all, which is how a kind documented as defaulting to Domain
/// leaves every lesson already in the store invisible to a reader looking for Domain.
/// </summary>
public static class LessonDefaults
{
    /// <summary>
    /// What the lesson is about, with the absent case resolved. Absent reads as Domain: a lesson
    /// minted before this field existed is a fact about the software its task was building, because
    /// there was nowhere to record a fact about the kernel. Every read of a stored kind goes through
    /// here rather than through the nullable field, so the default holds at the boundary and not
    /// only in a comment.
    /// </summary>
    public static LessonKind EffectiveKind(this Lesson lesson)
    {
        ArgumentNullException.ThrowIfNull(lesson);
        return lesson.Kind ?? LessonKind.Domain;
    }
}
