using AILedger.Core.Claims;
using AILedger.Core.Challenges;
using AILedger.Core.Contracts;
using AILedger.Core.Decisions;
using AILedger.Core.Evidences;
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
            RunStarted started => StartRun(Require(state), @event, started),
            RunCompleted completed => CompleteRun(Require(state), completed),
            StagePrerequisitesWaived waived => RecordStagePrerequisiteWaiver(Require(state), @event, waived),
            StageTransitioned transitioned => TransitionStage(Require(state), transitioned),
            EscalationRaised raised => Require(state) with { Escalations = Set(Require(state).Escalations, raised.Escalation.Id, raised.Escalation) },
            EscalationResolved resolved => ResolveEscalation(Require(state), resolved),
            AlternativeRecorded recorded => Require(state) with { Alternatives = Set(Require(state).Alternatives, recorded.Alternative.Id, recorded.Alternative) },
            ConstraintAdded added => Require(state) with { Constraints = Set(Require(state).Constraints, added.Constraint.Id, added.Constraint) },
            ConstraintSuperseded superseded => SupersedeConstraint(Require(state), superseded),
            WorkItemCompleted completed => WorkItemStateProjector.Complete(Require(state), completed),
            WorkItemBlocked blocked => WorkItemStateProjector.Block(Require(state), blocked),
            WorkItemUnblocked unblocked => WorkItemStateProjector.Unblock(Require(state), unblocked),
            WorkItemAbandoned abandoned => WorkItemStateProjector.Abandon(Require(state), abandoned),
            ClaimDependenciesRepointed repointed => ClaimStateProjector.RepointDependencies(Require(state), repointed),
            DecisionOverturned overturned => DecisionStateProjector.Overturn(Require(state), overturned),
            LessonMinted minted => AddLesson(Require(state), minted.Lesson),
            LessonRecalled recalled => AddLesson(Require(state), recalled.Lesson),
            LessonMarked marked => AddLessonMark(Require(state), marked.Mark),
            ArtifactRecorded recorded => Require(state) with
            {
                Artifacts = Set(Require(state).Artifacts, recorded.Artifact.ArtifactId, recorded.Artifact)
            },
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
            // The bracket, projected so that the runs pointing back at it can be selected without
            // re-reading the log. The end is written onto the same record rather than kept as a
            // second one: a session with no EndedAt is open, and an open bracket is why measures 1
            // and 2 report an absent duration instead of measuring against a clock this projection
            // does not take.
            SessionStarted started => Require(state) with
            {
                CoordinatorSessions = Set(
                    Require(state).CoordinatorSessions, started.Session.Id, started.Session)
            },
            SessionCompleted completed => CompleteCoordinatorSession(Require(state), completed),
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

    private static GovernedTaskState AddLesson(GovernedTaskState state, Lesson lesson) =>
        state with { Lessons = Set(state.Lessons, lesson.Id, lesson) };

    private static GovernedTaskState AddLessonMark(GovernedTaskState state, LessonMark mark) =>
        state with { LessonMarks = Set(state.LessonMarks, mark.Id, mark) };

    private static GovernedTaskState StartRun(GovernedTaskState state, LedgerEvent @event, RunStarted started)
    {
        var projected = WorkItemStateProjector.ActivateForRun(state, started.Run);

        return projected with
        {
            Runs = Set(state.Runs, started.Run.Id, started.Run),
            ContextBriefWaivers = JoinBriefWaiver(
                state, @event, ContextBriefWaiver.ProviderLaunchKind, started.Run.Id.Value)
        };
    }

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

    private static GovernedTaskState CompleteRun(GovernedTaskState state, RunCompleted completed)
    {
        // Every field this payload puts on the run, and the mapping itself, are in
        // RunCompletionProjection — shared with the memory index's replay of the same log. It used to
        // be written out here and again there, and the two drifted by ten fields (VC3, VE7).
        var run = RunCompletionProjection.Apply(state.Runs[completed.RunId], completed);

        var projected = WorkItemStateProjector.PauseAfterRun(state, run);

        return projected with
        {
            Runs = Set(state.Runs, completed.RunId, run)
        };
    }

    private static GovernedTaskState CompleteCoordinatorSession(
        GovernedTaskState state,
        SessionCompleted completed)
    {
        var session = state.CoordinatorSessions[completed.SessionId] with { EndedAt = completed.EndedAt };
        return state with
        {
            CoordinatorSessions = Set(state.CoordinatorSessions, completed.SessionId, session)
        };
    }

    private static GovernedTaskState ResolveEscalation(GovernedTaskState state, EscalationResolved resolved)
    {
        var escalation = state.Escalations[resolved.EscalationId] with
        {
            Status = resolved.Status,
            Resolution = resolved.Resolution,
            ResolvedBy = resolved.ResolvedBy
        };

        return state with { Escalations = Set(state.Escalations, resolved.EscalationId, escalation) };
    }

    private static GovernedTaskState SupersedeConstraint(GovernedTaskState state, ConstraintSuperseded superseded)
    {
        var constraint = state.Constraints[superseded.ConstraintId] with { Status = ConstraintStatus.Superseded };
        return state with { Constraints = Set(state.Constraints, superseded.ConstraintId, constraint) };
    }

    private static GovernedTaskState TransitionStage(GovernedTaskState state, StageTransitioned transitioned)
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

    private static GovernedTaskState RecordStagePrerequisiteWaiver(
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
