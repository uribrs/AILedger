using AILedger.Core.Contracts;

namespace AILedger.Core.Domain;

public sealed class TaskReducer : ITaskReducer
{
    public GovernedTaskState Apply(GovernedTaskState? state, LedgerEvent @event)
    {
        ValidateEnvelope(state, @event);

        var next = @event.Data switch
        {
            TaskOpened opened => OpenTask(@event, opened),
            RoleAssigned assigned => Require(state) with { Roles = Set(Require(state).Roles, assigned.Assignment.ActorId, assigned.Assignment) },
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
            StageTransitioned transitioned => TransitionStage(Require(state), transitioned),
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

    private static GovernedTaskState OpenTask(LedgerEvent @event, TaskOpened opened) => new()
    {
        TaskId = @event.TaskId,
        Title = opened.Title,
        Goal = opened.Goal
    };

    private static GovernedTaskState ResolveClaim(GovernedTaskState state, ClaimResolved resolved)
    {
        var claim = state.Claims[resolved.ClaimId] with
        {
            Status = resolved.Status,
            EvidenceIds = resolved.EvidenceIds.ToArray()
        };

        return state with { Claims = Set(state.Claims, resolved.ClaimId, claim) };
    }

    private static GovernedTaskState ResolveDecision(GovernedTaskState state, DecisionResolved resolved)
    {
        var decision = state.Decisions[resolved.DecisionId] with { Status = resolved.Status };
        return state with { Decisions = Set(state.Decisions, resolved.DecisionId, decision) };
    }

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
            EndedAt = completed.EndedAt
        };

        var workItems = state.WorkItems;
        if (run.WorkItemId is { } workItemId)
        {
            var currentWorkItem = state.WorkItems[workItemId];
            if (currentWorkItem.Status is not (WorkItemStatus.Blocked or WorkItemStatus.Stale))
            {
                var status = completed.Status == AgentRunStatus.Completed
                    ? WorkItemStatus.Completed
                    : WorkItemStatus.Paused;
                workItems = Set(workItems, workItemId, currentWorkItem with { Status = status });
            }
        }

        return state with
        {
            Runs = Set(state.Runs, completed.RunId, run),
            WorkItems = workItems
        };
    }

    private static GovernedTaskState TransitionStage(GovernedTaskState state, StageTransitioned transitioned)
    {
        if (state.Stage != transitioned.Previous)
        {
            throw new GovernanceException(
                $"Stage transition expected '{transitioned.Previous}', but task is in '{state.Stage}'.");
        }

        StageTransitionPolicy.EnsureAllowed(transitioned.Previous, transitioned.Current);
        return state with { Stage = transitioned.Current };
    }

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
