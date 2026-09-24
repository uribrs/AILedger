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
        // Episode markers for the lesson consultation arms. The reducer stamps Version after this
        // returns, so the event's own version is state.Version + 1 here.
        var version = state.Version + 1;
        return state with
        {
            Stage = transitioned.Current,
            PendingStagePrerequisiteWaiver = null,
            ResearchOpenedAtVersion = transitioned.Current == TaskStage.Research
                ? version
                : state.ResearchOpenedAtVersion,
            // PD9: a return from a stage after Design into Design or Research opens a reconsideration
            // episode. Design->Research does not (Previous is not after Design), so a detour keeps the
            // current value. Never cleared (PALT13).
            ReconsiderationOpenedAtVersion = (transitioned.Current is TaskStage.Design or TaskStage.Research) &&
                                             StageTransitionPolicy.IsBackward(transitioned.Previous, TaskStage.Design)
                ? version
                : state.ReconsiderationOpenedAtVersion
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
