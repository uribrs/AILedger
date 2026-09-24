using AILedger.Core.Contracts;

namespace AILedger.Core.Lessons;

// Applies Lesson events to the aggregate projection. Command decisions stay in the marking and
// minting rules; historical-event admissibility stays in LessonEventValidator.
internal static class LessonStateProjector
{
    internal static GovernedTaskState Add(GovernedTaskState state, LessonMinted minted) =>
        Add(state, minted.Lesson);

    internal static GovernedTaskState Add(GovernedTaskState state, LessonRecalled recalled) =>
        Add(state, recalled.Lesson);

    // New lessons join state.Lessons through the same Add as recall and minting, which is what makes
    // them citable by --from-lesson without touching the citation rules (PC2).
    internal static GovernedTaskState Consult(
        GovernedTaskState state,
        LedgerEvent @event,
        LessonsConsulted consulted)
    {
        var next = consulted.NewLessons.Aggregate(state, Add);
        var consultation = new LessonConsultation(
            @event.EventId,
            state.Version + 1,
            @event.ActorId,
            consulted.RunId,
            consulted.Purpose,
            consulted.Question,
            consulted.Tags,
            consulted.ClaimIds,
            consulted.ClaimSetHash,
            consulted.ServedLessonIds,
            @event.RecordedAt);
        return next with { LessonConsultations = [.. state.LessonConsultations, consultation] };
    }

    internal static GovernedTaskState AddMark(GovernedTaskState state, LessonMarked marked) =>
        state with { LessonMarks = Set(state.LessonMarks, marked.Mark.Id, marked.Mark) };

    private static GovernedTaskState Add(GovernedTaskState state, Lesson lesson) =>
        state with { Lessons = Set(state.Lessons, lesson.Id, lesson) };

    private static IReadOnlyDictionary<TKey, TValue> Set<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> source,
        TKey key,
        TValue value)
        where TKey : notnull
    {
        var copy = new Dictionary<TKey, TValue>(source)
        {
            [key] = value
        };
        return copy;
    }
}
