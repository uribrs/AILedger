using AILedger.Core.Alternatives;
using AILedger.Core.Artifacts;
using AILedger.Core.Claims;
using AILedger.Core.Challenges;
using AILedger.Core.Constraints;
using AILedger.Core.Contracts;
using AILedger.Core.CoordinatorSessions;
using AILedger.Core.Decisions;
using AILedger.Core.Evidences;
using AILedger.Core.Escalations;
using AILedger.Core.Lessons;
using AILedger.Core.Runs;
using AILedger.Core.Stages;
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
            TaskOpened opened => OpenTask(@event, opened),
            RoleAssigned assigned => AssignRole(Require(state), assigned.Assignment),
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
                JoinBriefWaiver(
                    Require(state), @event, ContextBriefWaiver.WorkItemKind, added.WorkItem.Id.Value)),
            WorkItemInvalidated invalidated => WorkItemStateProjector.Invalidate(Require(state), invalidated),
            RunStarted started => RunStateProjector.Start(
                Require(state),
                started,
                JoinBriefWaiver(
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
            // Keyed by actor, so a later brief replaces the one before it. The gate asks whether
            // this actor is briefed against the layer as it stands, and the answer is the last
            // brief; the earlier ones stay in the log, which is where history belongs.
            //
            // built.WorkItemId is deliberately not projected. An event is appended only when the
            // skill set is new or changed, so a work item carried here would be the one from
            // whichever invocation happened to append and would stay wrong for every later build
            // (MD2, SC3). The event keeps it; the projection answers a narrower question.
            ContextBuilt built => Require(state) with
            {
                ContextBuilds = Set(
                    Require(state).ContextBuilds,
                    @event.ActorId,
                    new ContextBuild(@event.ActorId, built.Role, built.Skills, @event.RecordedAt))
            },
            // Held for one step, not projected here. The waiver says a gate was opened for the event
            // that follows it, and it is that event — work.added or run.started — that knows what the
            // opening bought, so the two are joined there and the justification stays recorded once.
            ContextBriefWaived waived => Require(state) with
            {
                PendingContextBriefWaiver = new PendingBriefWaiver(
                    @event.EventId,
                    @event.ActorId,
                    waived.OperatorReason,
                    waived.StaleBriefEvidenceId,
                    waived.Provenance)
            },
            SessionStarted started => CoordinatorSessionStateProjector.Start(Require(state), started),
            SessionCompleted completed => CoordinatorSessionStateProjector.Complete(Require(state), completed),
            _ => throw new GovernanceException($"Unsupported event data '{@event.Data.GetType().Name}'.")
        };

        return next with
        {
            Version = (state?.Version ?? 0) + 1,
            // Any event other than a waiver clears the pending one, consumed or not. A waiver whose
            // command failed after it was appended must not attach itself to a later work item.
            PendingContextBriefWaiver =
                @event.Data is ContextBriefWaived ? next.PendingContextBriefWaiver : null
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

    private static GovernedTaskState OpenTask(LedgerEvent @event, TaskOpened opened)
    {
        return new GovernedTaskState
        {
            TaskId = @event.TaskId,
            Title = opened.Title,
            Goal = opened.Goal,
            Tags = opened.Tags,
            PendingOpeningActor = @event.ActorId
        };
    }

    private static GovernedTaskState AssignRole(GovernedTaskState state, RoleAssignment assignment) =>
        state with
        {
            Roles = Set(state.Roles, assignment.ActorId, assignment),
            PendingOpeningActor = null
        };

    // The waiver this event was let through by, if it was let through by one. The join is causationId:
    // CommandHandler chains a command's second event to its first, and a waiver and the work.added or
    // run.started beside it are exactly that pair. An event naming a different cause was not carried
    // by the pending waiver, so it records nothing — a missing join is the honest answer.
    private static IReadOnlyList<ContextBriefWaiver> JoinBriefWaiver(
        GovernedTaskState state,
        LedgerEvent @event,
        string kind,
        string targetId) =>
        state.PendingContextBriefWaiver is { } waiver && @event.CausationId == waiver.EventId
            ?
            [
                .. state.ContextBriefWaivers,
                new ContextBriefWaiver(
                    kind,
                    targetId,
                    waiver.ActorId,
                    waiver.OperatorReason,
                    waiver.StaleBriefEvidenceId,
                    waiver.Provenance)
            ]
            : state.ContextBriefWaivers;

    private static GovernedTaskState Require(GovernedTaskState? state) =>
        state ?? throw new GovernanceException("Task has not been opened.");

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
