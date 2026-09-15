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
