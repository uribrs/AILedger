using AILedger.Core.Application;
using AILedger.Core.Contracts;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Alternatives;

// Decides which Alternative events a command may emit. Historical-event admissibility stays in
// AlternativeEventValidator so command-time rules can tighten without invalidating existing logs.
internal static class AlternativeRules
{
    internal static IReadOnlyList<LedgerEventData> Record(
        GovernedTaskState state,
        RecordAlternativeCommand command,
        DateTimeOffset now)
    {
        RequireId(command.AlternativeId.Value, nameof(command.AlternativeId));
        EnsureNew(state.Alternatives, command.AlternativeId, "alternative");
        RequireText(command.Statement, nameof(command.Statement));
        RequireText(command.RejectionRationale, nameof(command.RejectionRationale));
        LessonCitationRules.EnsureCitedLessonWasRecalled(state, command.FromLesson);

        if (command.ReplacedByDecisionId is { } decisionId)
        {
            _ = Get(state.Decisions, decisionId, "decision");
        }

        var alternative = new Alternative(
            command.AlternativeId,
            command.Statement.Trim(),
            command.RejectionRationale.Trim(),
            command.ReplacedByDecisionId,
            new Provenance(command.ActorId, now, "alternative.record"),
            command.FromLesson);
        return [new AlternativeRecorded(alternative)];
    }
}
