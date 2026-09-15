using AILedger.Core.Application;
using AILedger.Core.Claims;
using AILedger.Core.Contracts;
using AILedger.Core.ContextBriefing;
using AILedger.Core.CoordinatorSessions;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.WorkItems;

// Command-time lifecycle policy. Historical-event counterparts live in WorkItemEventValidator;
// they intentionally do not repeat newer coordination gates that would invalidate older ledgers.
internal static class WorkItemLifecycleRules
{
    internal static IReadOnlyList<LedgerEventData> Add(
        GovernedTaskState state,
        AddWorkItemCommand command)
    {
        var briefWaiver = ContextGateRules.EnsureBriefed(
            state, command.ActorId, command.SkillsServedNow, "add work",
            command.WithoutBriefReason, command.StaleBriefEvidenceId);
        RequireId(command.WorkItemId.Value, nameof(command.WorkItemId));
        EnsureNew(state.WorkItems, command.WorkItemId, "work item");
        RequireText(command.Title, nameof(command.Title));
        EnsureUnique(command.DependsOnClaims, "Dependent claim IDs");
        EnsureReferencesExist(state.Claims, command.DependsOnClaims, "claim");
        ClaimDependencyRules.EnsureDependenciesAreCurrent(state, command.DependsOnClaims);
        EnsureScopeIsValid(state, command);

        if (command.Owner is { } owner && !state.Roles.ContainsKey(owner))
        {
            throw new GovernanceException($"Work owner '{owner}' has no assigned role.");
        }

        var workItem = new WorkItem(
            command.WorkItemId,
            command.Title.Trim(),
            command.Owner,
            WorkItemStatus.Proposed,
            command.DependsOnClaims.ToArray(),
            command.ResourceScope.Select(item => item.Trim()).ToArray(),
            NotSplitJustification: command.NotSplitJustification,
            BaseRef: string.IsNullOrWhiteSpace(command.BaseRef) ? null : command.BaseRef.Trim());

        return briefWaiver is null
            ? [new WorkItemAdded(workItem)]
            : [briefWaiver, new WorkItemAdded(workItem)];
    }

    internal static IReadOnlyList<LedgerEventData> Complete(
        GovernedTaskState state,
        CompleteWorkItemCommand command)
    {
        var workItem = Get(state.WorkItems, command.WorkItemId, "work item");
        EnsureCanComplete(state, workItem);
        var waiver = EnsureVerificationOrWaiver(state, command);

        return
        [
            new WorkItemCompleted(
                command.WorkItemId,
                waiver,
                waiver is null ? null : CoordinatorSessionWaiverAttribution.Resolve(state, command.ActorId))
        ];
    }

    internal static IReadOnlyList<LedgerEventData> Abandon(
        GovernedTaskState state,
        AbandonWorkItemCommand command)
    {
        var workItem = Get(state.WorkItems, command.WorkItemId, "work item");
        RequireText(command.Reason, nameof(command.Reason));

        if (workItem.Status is WorkItemStatus.Completed or WorkItemStatus.Stale)
        {
            throw new GovernanceException("A completed or stale work item cannot be abandoned.");
        }

        if (workItem.Status is WorkItemStatus.Abandoned)
        {
            throw new GovernanceException("Work item is already abandoned.");
        }

        EnsureNoActiveCoordination(
            state,
            command.WorkItemId,
            "abandoned");
        return [new WorkItemAbandoned(command.WorkItemId, command.Reason.Trim())];
    }

    internal static IReadOnlyList<LedgerEventData> Block(
        GovernedTaskState state,
        BlockWorkItemCommand command)
    {
        var workItem = Get(state.WorkItems, command.WorkItemId, "work item");
        RequireText(command.Reason, nameof(command.Reason));
        if (workItem.Status is WorkItemStatus.Completed or WorkItemStatus.Abandoned or WorkItemStatus.Stale)
        {
            throw new GovernanceException("A completed, abandoned or stale work item cannot be blocked.");
        }

        if (command.EscalationId is { } escalationId)
        {
            var escalation = Get(state.Escalations, escalationId, "escalation");
            if (escalation.Status != EscalationStatus.Open)
            {
                throw new GovernanceException("A work item can only be blocked on an open escalation.");
            }
        }

        return [new WorkItemBlocked(command.WorkItemId, command.Reason.Trim(), command.EscalationId)];
    }

    internal static IReadOnlyList<LedgerEventData> Unblock(
        GovernedTaskState state,
        UnblockWorkItemCommand command)
    {
        var workItem = Get(state.WorkItems, command.WorkItemId, "work item");
        if (workItem.Status != WorkItemStatus.Blocked)
        {
            throw new GovernanceException(
                $"Only a blocked work item can be unblocked; this one is '{workItem.Status}'.");
        }

        var invalid = workItem.DependsOnClaims
            .Select(claimId => state.Claims[claimId])
            .FirstOrDefault(claim => claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded);
        if (invalid is not null)
        {
            throw new GovernanceException(
                $"Work item depends on '{invalid.Status.ToString().ToLowerInvariant()}' claim '{invalid.Id}'. " +
                "Add a replacement work item on a current claim instead of unblocking this one.");
        }

        return [new WorkItemUnblocked(command.WorkItemId)];
    }

    internal static void EnsureCanStart(
        GovernedTaskState state,
        ActorId actorId,
        WorkItem workItem)
    {
        if (workItem.Owner is null || workItem.Owner == actorId || RoleAssignmentRules.IsOperator(state, actorId))
        {
            return;
        }

        throw new GovernanceException(
            $"Only work owner '{workItem.Owner}' or an operator can start work item '{workItem.Id}'.");
    }

    private static void EnsureScopeIsValid(GovernedTaskState state, AddWorkItemCommand command)
    {
        EnsureUnique(command.ResourceScope, "Resource scope entries", StringComparer.Ordinal);
        if (command.ResourceScope.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("Resource scope entries cannot be empty.");
        }

        if (command.ResourceScope.Any(scope => !Path.IsPathFullyQualified(scope.Trim())))
        {
            throw new GovernanceException("Resource scope entries must be absolute paths.");
        }

        EnsureMultipleScopesAreJustified(state, command);
        WorkItemScopeRules.EnsureAvailable(state, command.WorkItemId, command.ResourceScope);
    }

    private static void EnsureMultipleScopesAreJustified(
        GovernedTaskState state,
        AddWorkItemCommand command)
    {
        if (command.ResourceScope.Count <= 1)
        {
            return;
        }

        if (command.NotSplitJustification is not { } justificationId)
        {
            throw new GovernanceException(
                $"Work item '{command.WorkItemId}' claims more than one area. Record why they were not " +
                "split into separate work items, and name that alternative.");
        }

        _ = Get(state.Alternatives, justificationId, "alternative");
    }

    private static void EnsureCanComplete(GovernedTaskState state, WorkItem workItem)
    {
        if (workItem.Status is WorkItemStatus.Completed)
        {
            throw new GovernanceException("Work item is already completed.");
        }

        if (workItem.Status is WorkItemStatus.Abandoned)
        {
            throw new GovernanceException(
                "An abandoned work item cannot be completed. Add a new work item instead of reviving this one.");
        }

        if (workItem.Status is WorkItemStatus.Blocked or WorkItemStatus.Stale)
        {
            throw new GovernanceException(
                $"Work item in status '{workItem.Status}' cannot be completed before it is repaired.");
        }

        EnsureNoActiveCoordination(state, workItem.Id, "completed");
    }

    private static string? EnsureVerificationOrWaiver(
        GovernedTaskState state,
        CompleteWorkItemCommand command)
    {
        var waiver = TrimOrNull(command.WithoutVerificationReason);
        if (command.WithoutVerificationReason is not null && waiver is null)
        {
            throw new GovernanceException(
                "Waiving the required runs needs a reason; a blank waiver records nothing.");
        }

        if (waiver is not null)
        {
            if (!RoleAssignmentRules.IsOperator(state, command.ActorId))
            {
                throw new GovernanceException("Only an operator can complete work without the required runs.");
            }

            return waiver;
        }

        EnsureVerified(state, command.WorkItemId);
        return null;
    }

    private static void EnsureVerified(GovernedTaskState state, WorkItemId workItemId)
    {
        if (!WorkItemVerificationRules.HasCompletedWorkingRun(state, workItemId))
        {
            throw new GovernanceException(
                $"Work item '{workItemId}' has no completed run by a working role and cannot be " +
                "completed. A Worker or Researcher run does the work; a coordinating role's run does " +
                "not. An operator may complete it without one by recording why.");
        }

        if (!WorkItemVerificationRules.HasCompletedVerifierRun(state, workItemId))
        {
            throw new GovernanceException(
                $"Work item '{workItemId}' has no completed verifier run and cannot be completed. " +
                "An operator may complete it without one by recording why.");
        }

        if (!WorkItemVerificationRules.HasVerifierRunAfterLatestWork(state, workItemId))
        {
            throw new GovernanceException(
                $"Work item '{workItemId}' was verified before its latest working run finished, " +
                "so no verifier run has seen the finished work. Verify it again, or an operator may " +
                "complete it without doing so by recording why.");
        }

        if (WorkItemVerificationRules.ProviderThatVerifiedItsOwnWork(state, workItemId) is { } sameProvider)
        {
            throw new GovernanceException(
                $"Work item '{workItemId}' was verified by the same provider that did the work " +
                $"('{sameProvider}'), so nothing independent has read it. Verify it with a different " +
                "provider, or an operator may complete it without doing so by recording why.");
        }
    }

    private static void EnsureNoActiveCoordination(
        GovernedTaskState state,
        WorkItemId workItemId,
        string transition)
    {
        if (state.Runs.Values.Any(run =>
                run.WorkItemId == workItemId && run.Status is AgentRunStatus.Active))
        {
            throw new GovernanceException($"Work item cannot be {transition} while a run is still active.");
        }

        if (state.Escalations.Values.Any(escalation =>
                escalation.WorkItemId == workItemId && escalation.Status == EscalationStatus.Open))
        {
            throw new GovernanceException($"Work item cannot be {transition} while an escalation on it is open.");
        }
    }
}
