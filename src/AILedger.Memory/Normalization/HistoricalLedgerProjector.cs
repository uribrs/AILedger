using AILedger.Core.Contracts;
using System.IO;

namespace AILedger.Memory.Normalization;

/// <summary>
/// Projects already-recorded event payloads for the disposable memory read model.
/// It deliberately does not apply the kernel's current command-time authority rules:
/// older histories may predate their role assignments, while the event file remains
/// the canonical source of the payload and provenance being indexed.
/// </summary>
internal static class HistoricalLedgerProjector
{
    public static GovernedTaskState Apply(GovernedTaskState? state, LedgerEvent @event)
    {
        ValidateEnvelope(state, @event);

        var next = @event.Data switch
        {
            TaskOpened opened => new GovernedTaskState
            {
                TaskId = @event.TaskId,
                Title = opened.Title,
                Goal = opened.Goal,
                Tags = opened.Tags
            },
            RoleAssigned assigned => Require(state) with
            {
                Roles = Set(Require(state).Roles, assigned.Assignment.ActorId, assigned.Assignment)
            },
            ClaimAdded added => Require(state) with
            {
                Claims = Set(Require(state).Claims, added.Claim.Id, added.Claim)
            },
            ClaimResolved resolved => ResolveClaim(Require(state), resolved),
            EvidenceAdded added => Require(state) with
            {
                Evidence = Set(Require(state).Evidence, added.Evidence.Id, added.Evidence)
            },
            DecisionProposed proposed => Require(state) with
            {
                Decisions = Set(Require(state).Decisions, proposed.Decision.Id, proposed.Decision)
            },
            DecisionResolved resolved => ResolveDecision(Require(state), resolved),
            DecisionInvalidated invalidated => SetDecisionStatus(
                Require(state), invalidated.DecisionId, DecisionStatus.Invalidated),
            ChallengeRaised raised => Require(state) with
            {
                Challenges = Set(Require(state).Challenges, raised.Challenge.Id, raised.Challenge)
            },
            ChallengeDisposed disposed => DisposeChallenge(Require(state), disposed),
            WorkItemAdded added => Require(state) with
            {
                WorkItems = Set(Require(state).WorkItems, added.WorkItem.Id, added.WorkItem)
            },
            WorkItemInvalidated invalidated => SetWorkItemStatus(
                Require(state), invalidated.WorkItemId, invalidated.Status,
                RequireWorkItem(state, invalidated.WorkItemId).BlockReason),
            RunStarted started => StartRun(Require(state), started.Run),
            RunCompleted completed => CompleteRun(Require(state), completed),
            StagePrerequisitesWaived => Require(state),
            // The context gate's two events are audit records: they prove a brief was served, or
            // that an operator deliberately proceeded without one. Neither changes the state this
            // index projects, so both are no-ops here — but the arm has to exist. This projector is
            // a third replay of the log alongside CommandHandler and TaskTransitionValidator, and
            // its switch throws on any event data it has no case for, so an event type added to the
            // kernel without an arm here stops the whole index rebuilding.
            //
            // A missing arm is the loud failure, and it is the only one this comment used to name.
            // The quiet one costs more. A new *field* on an event type whose arm already exists is
            // dropped in silence: no exception, no failing test, and this index then reports a value
            // the log holds as absent, which a reader takes for "nothing was recorded". That is what
            // happened to the launch limit and the provider's terminal reason, and to eight fields
            // before them (VC3, VE7). So when a field is added to an existing event, checking that
            // this reader has an arm is not the check — the check is whether that arm projects the
            // new field. CLAUDE.md's "every rule is written twice" says where to look and not what to
            // look for, and the surest answer is not to look at all: the run.completed mapping now
            // lives once, in RunCompletionProjection, and is shared with the canonical reducer.
            ContextBuilt => Require(state),
            ContextBriefWaived => Require(state),
            // The coordinating session's two boundary events, projected here for the same reason
            // the canonical reducer projects them: the runs a session dispatched point back at it,
            // and an index that dropped the bracket would hold children with a parent it never saw.
            //
            // This arm exists above all because the switch below throws on event data it has no case
            // for. That is not hypothetical: it stopped the memory index rebuilding on 2026-09-10,
            // and it is why the kernel's two readers and this one had to get their arms in one
            // change (attention item R3).
            SessionStarted started => Require(state) with
            {
                CoordinatorSessions = Set(
                    Require(state).CoordinatorSessions, started.Session.Id, started.Session)
            },
            SessionCompleted completed => CompleteCoordinatorSession(Require(state), completed),
            StageTransitioned transitioned => Require(state) with { Stage = transitioned.Current },
            EscalationRaised raised => Require(state) with
            {
                Escalations = Set(Require(state).Escalations, raised.Escalation.Id, raised.Escalation)
            },
            EscalationResolved resolved => ResolveEscalation(Require(state), resolved),
            AlternativeRecorded recorded => Require(state) with
            {
                Alternatives = Set(Require(state).Alternatives, recorded.Alternative.Id, recorded.Alternative)
            },
            ConstraintAdded added => Require(state) with
            {
                Constraints = Set(Require(state).Constraints, added.Constraint.Id, added.Constraint)
            },
            ConstraintSuperseded superseded => SupersedeConstraint(Require(state), superseded.ConstraintId),
            WorkItemCompleted completed => SetWorkItemStatus(
                Require(state), completed.WorkItemId, WorkItemStatus.Completed, null),
            WorkItemBlocked blocked => SetWorkItemStatus(
                Require(state), blocked.WorkItemId, WorkItemStatus.Blocked, blocked.Reason),
            WorkItemUnblocked unblocked => SetWorkItemStatus(
                Require(state), unblocked.WorkItemId, WorkItemStatus.Paused, null),
            WorkItemAbandoned abandoned => AbandonWorkItem(Require(state), abandoned),
            ClaimDependenciesRepointed repointed => RepointDependencies(Require(state), repointed),
            DecisionOverturned overturned => SetDecisionStatus(
                Require(state), overturned.DecisionId, DecisionStatus.Invalidated),
            LessonMinted minted => AddLesson(Require(state), minted.Lesson),
            LessonRecalled recalled => AddLesson(Require(state), recalled.Lesson),
            LessonMarked marked => Require(state) with
            {
                LessonMarks = Set(Require(state).LessonMarks, marked.Mark.Id, marked.Mark)
            },
            ArtifactRecorded recorded => Require(state) with
            {
                Artifacts = Set(Require(state).Artifacts, recorded.Artifact.ArtifactId, recorded.Artifact)
            },
            _ => throw new InvalidDataException(
                $"Unsupported historical event data '{@event.Data.GetType().Name}'.")
        };

        return next with { Version = (state?.Version ?? 0) + 1 };
    }

    private static void ValidateEnvelope(GovernedTaskState? state, LedgerEvent @event)
    {
        if (@event.SchemaVersion != GovernedTaskState.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported event schema version '{@event.SchemaVersion}'.");
        }

        if (state is not null && state.TaskId != @event.TaskId)
        {
            throw new InvalidDataException("Event task identity does not match the history being projected.");
        }

        if (state is null && @event.Data is not TaskOpened)
        {
            throw new InvalidDataException("The first historical event must open the task.");
        }

        if (state is not null && @event.Data is TaskOpened)
        {
            throw new InvalidDataException("A historical task cannot be opened twice.");
        }
    }

    private static GovernedTaskState ResolveClaim(GovernedTaskState state, ClaimResolved resolved)
    {
        var current = state.Claims[resolved.ClaimId];
        var evidence = current.EvidenceIds.Concat(resolved.EvidenceIds).Distinct().ToArray();
        var claim = current with
        {
            Status = resolved.Status,
            EvidenceIds = evidence,
            SupersededByClaimId = resolved.SupersededByClaimId ?? current.SupersededByClaimId
        };
        return state with { Claims = Set(state.Claims, resolved.ClaimId, claim) };
    }

    private static GovernedTaskState ResolveDecision(GovernedTaskState state, DecisionResolved resolved) =>
        SetDecisionStatus(state, resolved.DecisionId, resolved.Status);

    private static GovernedTaskState SetDecisionStatus(
        GovernedTaskState state,
        DecisionId id,
        DecisionStatus status) =>
        state with { Decisions = Set(state.Decisions, id, state.Decisions[id] with { Status = status }) };

    private static GovernedTaskState DisposeChallenge(GovernedTaskState state, ChallengeDisposed disposed) =>
        state with
        {
            Challenges = Set(state.Challenges, disposed.ChallengeId,
                state.Challenges[disposed.ChallengeId] with { Status = disposed.Status })
        };

    private static GovernedTaskState StartRun(GovernedTaskState state, AgentRun run)
    {
        var workItems = state.WorkItems;
        if (run.WorkItemId is { } workItemId)
        {
            workItems = Set(workItems, workItemId,
                workItems[workItemId] with { Status = WorkItemStatus.Active });
        }

        return state with { Runs = Set(state.Runs, run.Id, run), WorkItems = workItems };
    }

    private static GovernedTaskState CompleteRun(GovernedTaskState state, RunCompleted completed)
    {
        // The same mapping the canonical reducer applies, because it is the same event. This used to
        // be a second copy of it and had fallen ten fields behind — the launch limit, the provider's
        // terminal reason, every cost field, the truncated-line count and the served model — so this
        // index reported values the log holds as absent (VC3, VE7).
        var run = RunCompletionProjection.Apply(state.Runs[completed.RunId], completed);
        var workItems = state.WorkItems;
        if (run.WorkItemId is { } workItemId)
        {
            var work = workItems[workItemId];
            if (work.Status is not (WorkItemStatus.Blocked or WorkItemStatus.Stale or WorkItemStatus.Abandoned))
            {
                workItems = Set(workItems, workItemId, work with { Status = WorkItemStatus.Paused });
            }
        }

        return state with { Runs = Set(state.Runs, run.Id, run), WorkItems = workItems };
    }

    private static GovernedTaskState CompleteCoordinatorSession(
        GovernedTaskState state,
        SessionCompleted completed) =>
        state with
        {
            CoordinatorSessions = Set(state.CoordinatorSessions, completed.SessionId,
                state.CoordinatorSessions[completed.SessionId] with { EndedAt = completed.EndedAt })
        };

    private static GovernedTaskState ResolveEscalation(
        GovernedTaskState state,
        EscalationResolved resolved)
    {
        var escalation = state.Escalations[resolved.EscalationId] with
        {
            Status = resolved.Status,
            Resolution = resolved.Resolution,
            ResolvedBy = resolved.ResolvedBy
        };
        return state with
        {
            Escalations = Set(state.Escalations, resolved.EscalationId, escalation)
        };
    }

    private static GovernedTaskState SupersedeConstraint(GovernedTaskState state, ConstraintId id) =>
        state with
        {
            Constraints = Set(state.Constraints, id,
                state.Constraints[id] with { Status = ConstraintStatus.Superseded })
        };

    private static GovernedTaskState AbandonWorkItem(GovernedTaskState state, WorkItemAbandoned abandoned)
    {
        var work = state.WorkItems[abandoned.WorkItemId] with
        {
            Status = WorkItemStatus.Abandoned,
            AbandonReason = abandoned.Reason
        };
        return state with { WorkItems = Set(state.WorkItems, abandoned.WorkItemId, work) };
    }

    private static GovernedTaskState SetWorkItemStatus(
        GovernedTaskState state,
        WorkItemId id,
        WorkItemStatus status,
        string? blockReason) =>
        state with
        {
            WorkItems = Set(state.WorkItems, id,
                state.WorkItems[id] with { Status = status, BlockReason = blockReason })
        };

    private static GovernedTaskState RepointDependencies(
        GovernedTaskState state,
        ClaimDependenciesRepointed repointed)
    {
        var decisions = state.Decisions;
        foreach (var decision in decisions.Values
                     .Where(item => item.DependsOnClaims.Contains(repointed.SupersededClaimId))
                     .Where(item => item.Status is DecisionStatus.Proposed or DecisionStatus.Accepted))
        {
            decisions = Set(decisions, decision.Id, decision with
            {
                DependsOnClaims = Replace(
                    decision.DependsOnClaims, repointed.SupersededClaimId, repointed.ReplacementClaimId)
            });
        }

        var workItems = state.WorkItems;
        foreach (var work in workItems.Values
                     .Where(item => item.DependsOnClaims.Contains(repointed.SupersededClaimId))
                     .Where(item => item.Status is not (
                         WorkItemStatus.Stale or WorkItemStatus.Completed or WorkItemStatus.Abandoned)))
        {
            workItems = Set(workItems, work.Id, work with
            {
                DependsOnClaims = Replace(
                    work.DependsOnClaims, repointed.SupersededClaimId, repointed.ReplacementClaimId)
            });
        }

        return state with { Decisions = decisions, WorkItems = workItems };
    }

    private static IReadOnlyList<ClaimId> Replace(
        IReadOnlyList<ClaimId> claims,
        ClaimId from,
        ClaimId to) =>
        claims.Select(id => id == from ? to : id).Distinct().ToArray();

    private static GovernedTaskState AddLesson(GovernedTaskState state, Lesson lesson) =>
        state with { Lessons = Set(state.Lessons, lesson.Id, lesson) };

    private static GovernedTaskState Require(GovernedTaskState? state) =>
        state ?? throw new InvalidDataException("The historical task has not been opened.");

    private static WorkItem RequireWorkItem(GovernedTaskState? state, WorkItemId id) =>
        Require(state).WorkItems[id];

    private static IReadOnlyDictionary<TKey, TValue> Set<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> source,
        TKey key,
        TValue value)
        where TKey : notnull
    {
        var copy = new Dictionary<TKey, TValue>(source) { [key] = value };
        return copy;
    }
}
