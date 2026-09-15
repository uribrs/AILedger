using AILedger.Core.Alternatives;
using AILedger.Core.Artifacts;
using AILedger.Core.Claims;
using AILedger.Core.Challenges;
using AILedger.Core.Constraints;
using AILedger.Core.Contracts;
using AILedger.Core.CoordinatorSessions;
using AILedger.Core.ContextBriefing;
using AILedger.Core.Decisions;
using AILedger.Core.Evidences;
using AILedger.Core.Escalations;
using AILedger.Core.Lessons;
using AILedger.Core.Runs;
using AILedger.Core.Roles;
using AILedger.Core.Stages;
using AILedger.Core.TaskOpening;
using AILedger.Core.WorkItems;

namespace AILedger.Core.Domain;

public sealed class TaskReducer : ITaskReducer
{
    public GovernedTaskState Apply(GovernedTaskState? state, LedgerEvent @event)
    {
        ValidateEnvelope(state, @event);
        TaskTransitionValidator.Validate(state, @event);

        var next = @event.Data switch
        {
            TaskOpened opened => TaskOpeningStateProjector.Open(@event, opened),
            RoleAssigned assigned => RoleStateProjector.Assign(Require(state), assigned.Assignment),
            ClaimAdded added => ClaimStateProjector.Add(Require(state), added),
            ClaimResolved resolved => ClaimStateProjector.Resolve(Require(state), resolved),
            EvidenceAdded added => EvidenceStateProjector.Add(Require(state), added),
            DecisionProposed proposed => DecisionStateProjector.Add(Require(state), proposed),
            DecisionResolved resolved => DecisionStateProjector.Resolve(Require(state), resolved),
            DecisionInvalidated invalidated => DecisionStateProjector.Invalidate(Require(state), invalidated),
            ChallengeRaised raised => ChallengeStateProjector.Add(Require(state), raised),
            ChallengeDisposed disposed => ChallengeStateProjector.Dispose(Require(state), disposed),
            WorkItemAdded added => WorkItemStateProjector.Add(
                Require(state),
                added,
                ContextStateProjector.JoinWaiver(
                    Require(state), @event, ContextBriefWaiver.WorkItemKind, added.WorkItem.Id.Value)),
            WorkItemInvalidated invalidated => WorkItemStateProjector.Invalidate(Require(state), invalidated),
            RunStarted started => RunStateProjector.Start(
                Require(state),
                started,
                ContextStateProjector.JoinWaiver(
                    Require(state), @event, ContextBriefWaiver.ProviderLaunchKind, started.Run.Id.Value)),
            RunCompleted completed => RunStateProjector.Complete(Require(state), completed),
            StagePrerequisitesWaived waived =>
                StageStateProjector.RecordPrerequisiteWaiver(Require(state), @event, waived),
            StageTransitioned transitioned => StageStateProjector.Transition(Require(state), transitioned),
            EscalationRaised raised => EscalationStateProjector.Add(Require(state), raised),
            EscalationResolved resolved => EscalationStateProjector.Resolve(Require(state), resolved),
            AlternativeRecorded recorded => AlternativeStateProjector.Add(Require(state), recorded),
            ConstraintAdded added => ConstraintStateProjector.Add(Require(state), added),
            ConstraintSuperseded superseded => ConstraintStateProjector.Supersede(Require(state), superseded),
            WorkItemCompleted completed => WorkItemStateProjector.Complete(Require(state), completed),
            WorkItemBlocked blocked => WorkItemStateProjector.Block(Require(state), blocked),
            WorkItemUnblocked unblocked => WorkItemStateProjector.Unblock(Require(state), unblocked),
            WorkItemAbandoned abandoned => WorkItemStateProjector.Abandon(Require(state), abandoned),
            ClaimDependenciesRepointed repointed => ClaimStateProjector.RepointDependencies(Require(state), repointed),
            DecisionOverturned overturned => DecisionStateProjector.Overturn(Require(state), overturned),
            LessonMinted minted => LessonStateProjector.Add(Require(state), minted),
            LessonRecalled recalled => LessonStateProjector.Add(Require(state), recalled),
            LessonMarked marked => LessonStateProjector.AddMark(Require(state), marked),
            ArtifactRecorded recorded => ArtifactStateProjector.Add(Require(state), recorded),
            ContextBuilt built => ContextStateProjector.RecordBuild(Require(state), @event, built),
            ContextBriefWaived waived =>
                ContextStateProjector.RecordPendingWaiver(Require(state), @event, waived),
            SessionStarted started => CoordinatorSessionStateProjector.Start(Require(state), started),
            SessionCompleted completed => CoordinatorSessionStateProjector.Complete(Require(state), completed),
            _ => throw new GovernanceException($"Unsupported event data '{@event.Data.GetType().Name}'.")
        };

        return next with
        {
            Version = (state?.Version ?? 0) + 1,
            // Any event other than a waiver clears the pending one, consumed or not. A waiver whose
            // command failed after it was appended must not attach itself to a later work item.
            PendingContextBriefWaiver = ContextStateProjector.PendingWaiverAfter(@event.Data, next)
        };
    }

    private static void ValidateEnvelope(GovernedTaskState? state, LedgerEvent @event)
    {
        if (@event.SchemaVersion != GovernedTaskState.CurrentSchemaVersion)
        {
            throw new GovernanceException($"Unsupported event schema version '{@event.SchemaVersion}'.");
        }

        if (state is not null && state.TaskId != @event.TaskId)
        {
            throw new GovernanceException("Event task identity does not match the aggregate.");
        }

        if (state is null && @event.Data is not TaskOpened)
        {
            throw new GovernanceException("The first event for a task must open the task.");
        }

        if (state is not null && @event.Data is TaskOpened)
        {
            throw new GovernanceException("An opened task cannot be opened again.");
        }
    }

    private static GovernedTaskState Require(GovernedTaskState? state) =>
        state ?? throw new GovernanceException("Task has not been opened.");
}
