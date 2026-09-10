using AILedger.Core.Contracts;

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
            ClaimAdded added => Require(state) with { Claims = Set(Require(state).Claims, added.Claim.Id, added.Claim) },
            ClaimResolved resolved => ResolveClaim(Require(state), resolved),
            EvidenceAdded added => Require(state) with { Evidence = Set(Require(state).Evidence, added.Evidence.Id, added.Evidence) },
            DecisionProposed proposed => Require(state) with { Decisions = Set(Require(state).Decisions, proposed.Decision.Id, proposed.Decision) },
            DecisionResolved resolved => ResolveDecision(Require(state), resolved),
            DecisionInvalidated invalidated => InvalidateDecision(Require(state), invalidated),
            ChallengeRaised raised => Require(state) with { Challenges = Set(Require(state).Challenges, raised.Challenge.Id, raised.Challenge) },
            ChallengeDisposed disposed => DisposeChallenge(Require(state), disposed),
            WorkItemAdded added => Require(state) with { WorkItems = Set(Require(state).WorkItems, added.WorkItem.Id, added.WorkItem) },
            WorkItemInvalidated invalidated => InvalidateWorkItem(Require(state), invalidated),
            RunStarted started => StartRun(Require(state), started),
            RunCompleted completed => CompleteRun(Require(state), completed),
            StagePrerequisitesWaived waived => RecordStagePrerequisiteWaiver(Require(state), @event, waived),
            StageTransitioned transitioned => TransitionStage(Require(state), transitioned),
            EscalationRaised raised => Require(state) with { Escalations = Set(Require(state).Escalations, raised.Escalation.Id, raised.Escalation) },
            EscalationResolved resolved => ResolveEscalation(Require(state), resolved),
            AlternativeRecorded recorded => Require(state) with { Alternatives = Set(Require(state).Alternatives, recorded.Alternative.Id, recorded.Alternative) },
            ConstraintAdded added => Require(state) with { Constraints = Set(Require(state).Constraints, added.Constraint.Id, added.Constraint) },
            ConstraintSuperseded superseded => SupersedeConstraint(Require(state), superseded),
            WorkItemCompleted completed => SetWorkItemStatus(Require(state), completed.WorkItemId, WorkItemStatus.Completed, null),
            WorkItemBlocked blocked => SetWorkItemStatus(Require(state), blocked.WorkItemId, WorkItemStatus.Blocked, blocked.Reason),
            WorkItemUnblocked unblocked => SetWorkItemStatus(Require(state), unblocked.WorkItemId, WorkItemStatus.Paused, null),
            WorkItemAbandoned abandoned => AbandonWorkItem(Require(state), abandoned),
            ClaimDependenciesRepointed repointed => RepointDependencies(Require(state), repointed),
            DecisionOverturned overturned => OverturnDecision(Require(state), overturned),
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
            ContextBuilt built => Require(state) with
            {
                ContextBuilds = Set(
                    Require(state).ContextBuilds,
                    @event.ActorId,
                    new ContextBuild(
                        @event.ActorId, built.Role, built.WorkItemId, built.Skills, @event.RecordedAt))
            },
            // Nothing to project. The waiver says a gate was opened for the event that follows it,
            // and no rule asks the state whether one was: the stage waiver is held only because the
            // transition beside it has to be paired with it, and this one pairs with nothing.
            ContextBriefWaived => Require(state),
            _ => throw new GovernanceException($"Unsupported event data '{@event.Data.GetType().Name}'.")
        };

        return next with { Version = (state?.Version ?? 0) + 1 };
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

    private static GovernedTaskState ResolveClaim(GovernedTaskState state, ClaimResolved resolved)
    {
        var current = state.Claims[resolved.ClaimId];
        // R3 (claim-evidence-overwrite): evidence accumulates. A challenge rejecting a claim cites
        // only refuting evidence, and must not erase the supporting evidence already on record.
        var evidence = current.EvidenceIds
            .Concat(resolved.EvidenceIds)
            .Distinct()
            .ToArray();
        var claim = current with
        {
            Status = resolved.Status,
            EvidenceIds = evidence,
            SupersededByClaimId = resolved.SupersededByClaimId ?? current.SupersededByClaimId
        };

        return state with { Claims = Set(state.Claims, resolved.ClaimId, claim) };
    }

    private static GovernedTaskState ResolveDecision(GovernedTaskState state, DecisionResolved resolved)
    {
        var decision = state.Decisions[resolved.DecisionId] with { Status = resolved.Status };
        return state with { Decisions = Set(state.Decisions, resolved.DecisionId, decision) };
    }

    private static GovernedTaskState AddLesson(GovernedTaskState state, Lesson lesson) =>
        state with { Lessons = Set(state.Lessons, lesson.Id, lesson) };

    private static GovernedTaskState AddLessonMark(GovernedTaskState state, LessonMark mark) =>
        state with { LessonMarks = Set(state.LessonMarks, mark.Id, mark) };

    private static GovernedTaskState InvalidateDecision(GovernedTaskState state, DecisionInvalidated invalidated)
    {
        var decision = state.Decisions[invalidated.DecisionId] with { Status = DecisionStatus.Invalidated };
        return state with { Decisions = Set(state.Decisions, invalidated.DecisionId, decision) };
    }

    private static GovernedTaskState DisposeChallenge(GovernedTaskState state, ChallengeDisposed disposed)
    {
        var challenge = state.Challenges[disposed.ChallengeId] with { Status = disposed.Status };
        return state with { Challenges = Set(state.Challenges, disposed.ChallengeId, challenge) };
    }

    private static GovernedTaskState InvalidateWorkItem(GovernedTaskState state, WorkItemInvalidated invalidated)
    {
        var workItem = state.WorkItems[invalidated.WorkItemId] with { Status = invalidated.Status };
        return state with { WorkItems = Set(state.WorkItems, invalidated.WorkItemId, workItem) };
    }

    private static GovernedTaskState StartRun(GovernedTaskState state, RunStarted started)
    {
        var workItems = state.WorkItems;
        if (started.Run.WorkItemId is { } workItemId)
        {
            var workItem = state.WorkItems[workItemId] with { Status = WorkItemStatus.Active };
            workItems = Set(workItems, workItemId, workItem);
        }

        return state with
        {
            Runs = Set(state.Runs, started.Run.Id, started.Run),
            WorkItems = workItems
        };
    }

    private static GovernedTaskState CompleteRun(GovernedTaskState state, RunCompleted completed)
    {
        var run = state.Runs[completed.RunId] with
        {
            Status = completed.Status,
            ProviderSessionId = completed.ProviderSessionId,
            EndedAt = completed.EndedAt,
            // Absent on every run completed before these fields existed, and absent on a launch that
            // failed before its manifest was built. Both read as "no brief recorded", correctly.
            ManifestHash = completed.ManifestHash,
            ManifestArtifactCount = completed.ManifestArtifactCount,
            // Absent on every run completed before these existed, and absent on a run whose provider
            // stream produced no terminal event to read them from. Both read as "nobody measured
            // this", correctly. Turns is additionally absent for a provider that states no turn
            // count of its own, which is codex, and there the absence is the measurement (D4, IC1).
            Turns = completed.Turns,
            OutputTokens = completed.OutputTokens,
            MillisecondsToFirstLedgerWrite = completed.MillisecondsToFirstLedgerWrite,
            TokensInUncached = completed.TokensInUncached,
            TokensInCacheWrite = completed.TokensInCacheWrite,
            TokensInCacheRead = completed.TokensInCacheRead,
            // The served model wins over the requested one, because the record should say which
            // cognition ran rather than which was asked for. Absent leaves run.started's value
            // standing rather than erasing it.
            Model = completed.Model ?? state.Runs[completed.RunId].Model
        };

        var workItems = state.WorkItems;
        if (run.WorkItemId is { } workItemId)
        {
            var currentWorkItem = state.WorkItems[workItemId];
            // Abandoned joins them because Paused is a live status: flipping a released item back to
            // Paused would take its directory area back. Safe on old histories — no event recorded
            // before work.abandoned existed can produce this status.
            if (currentWorkItem.Status is not (WorkItemStatus.Blocked or WorkItemStatus.Stale
                or WorkItemStatus.Abandoned))
            {
                // A terminated provider process is not a claim that the work is done.
                // Completion is asserted explicitly through work.complete.
                workItems = Set(workItems, workItemId, currentWorkItem with { Status = WorkItemStatus.Paused });
            }
        }

        return state with
        {
            Runs = Set(state.Runs, completed.RunId, run),
            WorkItems = workItems
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

    private static GovernedTaskState RepointDependencies(GovernedTaskState state, ClaimDependenciesRepointed repointed)
    {
        var decisions = state.Decisions;
        // A terminal record's dependency list is the audit trail of what it was actually built on,
        // so a refinement leaves it alone. This is deliberately *not* the same set the correction
        // path selects: correction still stales a Completed dependent, refinement does not touch it.
        foreach (var decision in state.Decisions.Values
                     .Where(item => item.DependsOnClaims.Contains(repointed.SupersededClaimId))
                     .Where(item => item.Status is DecisionStatus.Proposed or DecisionStatus.Accepted))
        {
            decisions = Set(decisions, decision.Id, decision with
            {
                DependsOnClaims = Replace(decision.DependsOnClaims, repointed.SupersededClaimId, repointed.ReplacementClaimId)
            });
        }

        var workItems = state.WorkItems;
        foreach (var workItem in state.WorkItems.Values
                     .Where(item => item.DependsOnClaims.Contains(repointed.SupersededClaimId))
                     .Where(item => item.Status is not (WorkItemStatus.Stale or WorkItemStatus.Completed
                         or WorkItemStatus.Abandoned)))
        {
            workItems = Set(workItems, workItem.Id, workItem with
            {
                DependsOnClaims = Replace(workItem.DependsOnClaims, repointed.SupersededClaimId, repointed.ReplacementClaimId)
            });
        }

        return state with { Decisions = decisions, WorkItems = workItems };
    }

    private static IReadOnlyList<ClaimId> Replace(IReadOnlyList<ClaimId> claims, ClaimId from, ClaimId to) =>
        claims.Select(claimId => claimId == from ? to : claimId).Distinct().ToArray();

    private static GovernedTaskState OverturnDecision(GovernedTaskState state, DecisionOverturned overturned)
    {
        var decision = state.Decisions[overturned.DecisionId] with { Status = DecisionStatus.Invalidated };
        return state with { Decisions = Set(state.Decisions, overturned.DecisionId, decision) };
    }

    private static GovernedTaskState SupersedeConstraint(GovernedTaskState state, ConstraintSuperseded superseded)
    {
        var constraint = state.Constraints[superseded.ConstraintId] with { Status = ConstraintStatus.Superseded };
        return state with { Constraints = Set(state.Constraints, superseded.ConstraintId, constraint) };
    }

    // Kept apart from SetWorkItemStatus because the reason has to survive: why an area was given up
    // is the only thing left of the work once the item stops being touched.
    private static GovernedTaskState AbandonWorkItem(GovernedTaskState state, WorkItemAbandoned abandoned)
    {
        var workItem = state.WorkItems[abandoned.WorkItemId] with
        {
            Status = WorkItemStatus.Abandoned,
            AbandonReason = abandoned.Reason
        };
        return state with { WorkItems = Set(state.WorkItems, abandoned.WorkItemId, workItem) };
    }

    private static GovernedTaskState SetWorkItemStatus(
        GovernedTaskState state,
        WorkItemId workItemId,
        WorkItemStatus status,
        string? blockReason)
    {
        var workItem = state.WorkItems[workItemId] with { Status = status, BlockReason = blockReason };
        return state with { WorkItems = Set(state.WorkItems, workItemId, workItem) };
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
