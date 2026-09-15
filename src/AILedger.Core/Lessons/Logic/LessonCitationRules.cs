using AILedger.Core.Contracts;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Lessons;

// Replay counterpart: ReplayValidationRules.EnsureCitedLessonWasRecalled.
internal static class LessonCitationRules
{
    // The citation is checked against this task's own lessons, never against the store at
    // <home>/lessons. A foreign store is the one thing replay cannot re-read honestly: it changes
    // after the event is written, so a rule keyed on it would reject years-old history for a reason
    // that has nothing to do with what happened. state.Lessons carries every lesson this task
    // recalled or minted, so the check is local, and refusing a citation of a lesson the task never
    // saw is correct on its own terms — an agent cannot have been influenced by a lesson it was
    // never shown.
    internal static void EnsureCitedLessonWasRecalled(GovernedTaskState state, LessonId? fromLesson)
    {
        if (fromLesson is { } lessonId)
        {
            _ = Get(state.Lessons, lessonId, "recalled lesson");
        }
    }
}
