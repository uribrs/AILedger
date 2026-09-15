using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.Alternatives;

// Replay validates the event shape that command-time AlternativeRules emits. These checks remain
// separate because replay must continue accepting every history that was legal.
internal static class AlternativeEventValidator
{
    internal static void ValidateRecorded(
        GovernedTaskState state,
        LedgerEvent @event,
        Alternative alternative)
    {
        EnsureCitedLessonWasRecalled(state, alternative.FromLesson);
        RequireAuthority(state, @event.ActorId, Capability.RecordAlternative);
        EnsureNew(state.Alternatives, alternative.Id, "alternative");
        RequireId(alternative.Id.Value, nameof(alternative.Id));
        RequireText(alternative.Statement, nameof(alternative.Statement));
        RequireText(alternative.RejectionRationale, nameof(alternative.RejectionRationale));
        if (alternative.ReplacedByDecisionId is { } decisionId)
        {
            _ = Get(state.Decisions, decisionId, "decision");
        }

        ValidateProvenance(@event, alternative.Provenance, "alternative.record");
    }
}
