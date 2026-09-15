using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.Stages;

// Replay validates only invariants that are safe for every history ever accepted.
internal static class StageEventValidator
{
    internal static void ValidateTransitioned(
        GovernedTaskState state,
        LedgerEvent @event,
        StageTransitioned transitioned)
    {
        RequireAuthority(state, @event.ActorId, Capability.RequestTransition);
        RequireDefined(transitioned.Previous, nameof(transitioned.Previous));
        RequireDefined(transitioned.Current, nameof(transitioned.Current));
        if (state.Stage != transitioned.Previous)
        {
            throw new GovernanceException(
                $"Stage transition expected '{transitioned.Previous}', but task is in '{state.Stage}'.");
        }

        StageTransitionPolicy.EnsureAllowed(transitioned.Previous, transitioned.Current);
        if (state.PendingStagePrerequisiteWaiver is { } waiver)
        {
            if (waiver.ActorId != @event.ActorId ||
                waiver.TargetStage != transitioned.Current ||
                @event.CausationId != waiver.EventId)
            {
                throw new GovernanceException(
                    "A stage prerequisite waiver must be consumed by its immediately caused transition.");
            }
        }
        else
        {
            EnsureReplayPrerequisites(state, transitioned.Current);
        }

        // CommandHandler requires and emits lessons for a new Learn -> Archive command. Replay
        // deliberately does not require a preceding lesson: every archive history written before
        // lesson events existed carries none.

        // The transition-reason rule is command-time only, in all three of its halves, and only the
        // first half has to be. That a backward transition carries a reason keys on Reason being
        // absent, and almost every stage.transitioned in this ledger was written before the field
        // existed and carries none -- a replay copy would refuse histories that were legal when
        // they were written, which is the direction this file may never move in. The other two
        // halves -- that a forward transition carries no reason, and that a present reason is not
        // blank -- key on Reason being present, which no event written before the field can be, so
        // they are safe by construction and could have been written here. They were left out
        // deliberately rather than forgotten. What their absence costs is small and worth stating
        // plainly: a hand-edited events.jsonl line could replay a stage state the command path
        // would have refused to produce. Replay's protection against a forged line is provenance
        // and sequence, not a second copy of every rule.
    }

    internal static void ValidatePrerequisitesWaived(
        GovernedTaskState state,
        LedgerEvent @event,
        StagePrerequisitesWaived waived)
    {
        RequireAuthority(state, @event.ActorId, Capability.RequestTransition);
        RequireDefined(waived.TargetStage, nameof(waived.TargetStage));
        if (string.IsNullOrWhiteSpace(waived.Reason))
        {
            throw new GovernanceException(
                "Waiving stage prerequisites needs a reason; a blank waiver records nothing.");
        }

        if (!IsOperator(state, @event.ActorId))
        {
            throw new GovernanceException("Only an operator can transition stages without prerequisites.");
        }

        if (state.PendingStagePrerequisiteWaiver is not null)
        {
            throw new GovernanceException("A stage prerequisite waiver is already pending.");
        }

        StageTransitionPolicy.EnsureAllowed(state.Stage, waived.TargetStage);
    }

    private static void EnsureReplayPrerequisites(GovernedTaskState state, TaskStage target)
    {
        // The stage-engagement arms are command-time methodology rules. Adding them here would reject
        // histories that were legal before those prerequisites existed. These two structural gates
        // predate the arms and depend only on state every compatible history carries.
        if (target == TaskStage.Execution && state.WorkItems.Count == 0)
        {
            throw new GovernanceException("Execution requires at least one governed work item.");
        }

        if (target == TaskStage.Archive &&
            (state.Runs.Values.Any(run => run.Status is AgentRunStatus.Active) ||
             state.Challenges.Values.Any(challenge => challenge.Status == ChallengeStatus.Open)))
        {
            throw new GovernanceException("A task with active runs or open challenges cannot be archived.");
        }
    }
}
