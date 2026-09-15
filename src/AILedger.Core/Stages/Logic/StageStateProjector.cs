using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.Stages;

internal static class StageStateProjector
{
    internal static GovernedTaskState Transition(
        GovernedTaskState state,
        StageTransitioned transitioned)
    {
        if (state.Stage != transitioned.Previous)
        {
            throw new GovernanceException(
                $"Stage transition expected '{transitioned.Previous}', but task is in '{state.Stage}'.");
        }

        StageTransitionPolicy.EnsureAllowed(transitioned.Previous, transitioned.Current);
        return state with
        {
            Stage = transitioned.Current,
            PendingStagePrerequisiteWaiver = null
        };
    }

    internal static GovernedTaskState RecordPrerequisiteWaiver(
        GovernedTaskState state,
        LedgerEvent @event,
        StagePrerequisitesWaived waived) =>
        state with
        {
            PendingStagePrerequisiteWaiver = new StagePrerequisiteWaiver(
                @event.EventId,
                @event.ActorId,
                waived.TargetStage)
        };
}
