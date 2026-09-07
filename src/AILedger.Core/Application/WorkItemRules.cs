using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Replay counterparts: the TaskTransitionValidator.ValidateWorkItem* family.
internal static class WorkItemRules
{
    internal static IReadOnlyList<LedgerEventData> AddWorkItem(
        GovernedTaskState state,
        AddWorkItemCommand command)
    {
        RequireId(command.WorkItemId.Value, nameof(command.WorkItemId));
        EnsureNew(state.WorkItems, command.WorkItemId, "work item");
        RequireText(command.Title, nameof(command.Title));
        EnsureUnique(command.DependsOnClaims, "Dependent claim IDs");
        EnsureReferencesExist(state.Claims, command.DependsOnClaims, "claim");
        ClaimDependencyRules.EnsureDependenciesAreCurrent(state, command.DependsOnClaims);
        EnsureUnique(command.ResourceScope, "Resource scope entries", StringComparer.Ordinal);
    
        if (command.ResourceScope.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("Resource scope entries cannot be empty.");
        }
    
        if (command.ResourceScope.Any(scope => !Path.IsPathFullyQualified(scope.Trim())))
        {
            throw new GovernanceException("Resource scope entries must be absolute paths.");
        }
    
        // Two disjoint areas in one work item is one agent holding what two could have held. The
        // kernel cannot judge whether the split was right — that needs to know the work — but it can
        // refuse to let the choice not to split go unrecorded and unattributed.
        if (command.ResourceScope.Count > 1)
        {
            if (command.NotSplitJustification is not { } justificationId)
            {
                throw new GovernanceException(
                    $"Work item '{command.WorkItemId}' claims more than one area. Record why they were not " +
                    "split into separate work items, and name that alternative.");
            }
    
            _ = Get(state.Alternatives, justificationId, "alternative");
        }
    
        ScopeOccupancyRules.EnsureScopeIsNotAlreadyOccupied(state, command.WorkItemId, command.ResourceScope);
    
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
            NotSplitJustification: command.NotSplitJustification);
        return [new WorkItemAdded(workItem)];
    }

    internal static IReadOnlyList<LedgerEventData> CompleteWorkItem(
        GovernedTaskState state,
        CompleteWorkItemCommand command)
    {
        var workItem = Get(state.WorkItems, command.WorkItemId, "work item");
        if (workItem.Status is WorkItemStatus.Completed)
        {
            throw new GovernanceException("Work item is already completed.");
        }
    
        // Kept out of the "repair it first" refusal below: an abandoned item is not damaged and
        // there is nothing to repair. It was given up, and giving up is as terminal as finishing.
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
    
        // A provider exiting zero is not the same claim as the work being done, so the
        // run has to be closed before anyone can assert completion.
        if (HasOpenRun(state, command.WorkItemId))
        {
            throw new GovernanceException("Work item cannot be completed while a run is still active.");
        }
    
        if (HasOpenEscalation(state, command.WorkItemId))
        {
            throw new GovernanceException("Work item cannot be completed while an escalation on it is open.");
        }
    
        // The actor that did the work asserting the work is done is the same actor grading its own
        // homework, and a provider exiting zero says nothing either. Something adversarial has to
        // have looked at it. An operator may still overrule that, but only on the record.
        var waiver = TrimOrNull(command.WithoutVerificationReason);
        if (command.WithoutVerificationReason is not null && waiver is null)
        {
            throw new GovernanceException(
                "Waiving the required runs needs a reason; a blank waiver records nothing.");
        }
    
        if (waiver is null)
        {
            // A work item completed with no run at all means the kernel never saw who did the work,
            // or whether anyone did. Every work item completed in this task so far is that shape,
            // which is exactly how the gap went unnoticed for as long as it did.
            if (!HasCompletedWorkingRun(state, command.WorkItemId))
            {
                throw new GovernanceException(
                    $"Work item '{command.WorkItemId}' has no completed run by a working role and cannot be " +
                    "completed. An operator may complete it without one by recording why.");
            }
    
            if (!HasCompletedVerifierRun(state, command.WorkItemId))
            {
                throw new GovernanceException(
                    $"Work item '{command.WorkItemId}' has no completed verifier run and cannot be completed. " +
                    "An operator may complete it without one by recording why.");
            }
    
            // Separate from the check above so the refusal says which of the two failed. Having
            // been verified at some point and having been verified as finished are different
            // claims, and only the second one is worth anything.
            if (!HasVerifierRunAfterLatestWork(state, command.WorkItemId))
            {
                throw new GovernanceException(
                    $"Work item '{command.WorkItemId}' was verified before its latest working run finished, " +
                    "so no verifier run has seen the finished work. Verify it again, or an operator may " +
                    "complete it without doing so by recording why.");
            }
    
            // Separate again, for the same reason: having been verified after the work and having
            // been verified by someone who does not share the author's blind spots are different
            // claims. A model reviewing its own output agrees with it, so a verifier run on the
            // provider that did the work is a second opinion in name only.
            if (ProviderThatVerifiedItsOwnWork(state, command.WorkItemId) is { } sameProvider)
            {
                throw new GovernanceException(
                    $"Work item '{command.WorkItemId}' was verified by the same provider that did the work " +
                    $"('{sameProvider}'), so nothing independent has read it. Verify it with a different " +
                    "provider, or an operator may complete it without doing so by recording why.");
            }
        }
        else if (!RoleAssignmentRules.IsOperator(state, command.ActorId))
        {
            throw new GovernanceException("Only an operator can complete work without the required runs.");
        }
    
        return [new WorkItemCompleted(command.WorkItemId, waiver)];
    }
    
    // Work is released as well as finished. An item that turned out to be a dead end could not be
    // completed and could not be given up either, so it held its directory area against everyone
    // else for the rest of the task.

    internal static IReadOnlyList<LedgerEventData> AbandonWorkItem(
        GovernedTaskState state,
        AbandonWorkItemCommand command)
    {
        var workItem = Get(state.WorkItems, command.WorkItemId, "work item");
        RequireText(command.Reason, nameof(command.Reason));
        // Stale is here for a reason of its own: an already abandoned item that a later claim
        // rejection moved to Stale could be abandoned a second time, and the second reason
        // overwrote the first. The record of why an area was given up is the only thing left of
        // the work, so nothing may quietly replace it.
        if (workItem.Status is WorkItemStatus.Completed or WorkItemStatus.Stale)
        {
            throw new GovernanceException("A completed or stale work item cannot be abandoned.");
        }
    
        if (workItem.Status is WorkItemStatus.Abandoned)
        {
            throw new GovernanceException("Work item is already abandoned.");
        }
    
        // Same reason completion waits for the run to close: releasing the area under a live agent
        // would hand it to a second one while the first is still writing in it.
        if (HasOpenRun(state, command.WorkItemId))
        {
            throw new GovernanceException("Work item cannot be abandoned while a run is still active.");
        }
    
        // Abandoning is as terminal as completing, so it carries completion's escalation rule too.
        // An open escalation against an item nobody will ever work on is a question the log says is
        // still live and that nothing will ever answer: archive checks only active runs and open
        // challenges, so it would sit there forever. The operator holds both commands, and
        // withdrawing the escalation first says out loud that the question stopped mattering.
        if (HasOpenEscalation(state, command.WorkItemId))
        {
            throw new GovernanceException("Work item cannot be abandoned while an escalation on it is open.");
        }
    
        return [new WorkItemAbandoned(command.WorkItemId, command.Reason.Trim())];
    }

    internal static IReadOnlyList<LedgerEventData> BlockWorkItem(
        GovernedTaskState state,
        BlockWorkItemCommand command)
    {
        var workItem = Get(state.WorkItems, command.WorkItemId, "work item");
        RequireText(command.Reason, nameof(command.Reason));
        // Blocked is a live status that holds the item's area, so blocking a released item takes
        // that area back from whoever has since been given it. Stale belongs here with the other
        // two: it was the way back in, because a rejected claim moves a Completed or Abandoned item
        // to Stale and blocking it from there revived it on top of its successor. Refusing it also
        // fixes an older trap — a stale item that got blocked could never be unblocked, since
        // UnblockWorkItem refuses an item whose claim is rejected, so it held its area forever.
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

    internal static IReadOnlyList<LedgerEventData> UnblockWorkItem(
        GovernedTaskState state,
        UnblockWorkItemCommand command)
    {
        var workItem = Get(state.WorkItems, command.WorkItemId, "work item");
        if (workItem.Status != WorkItemStatus.Blocked)
        {
            throw new GovernanceException($"Only a blocked work item can be unblocked; this one is '{workItem.Status}'.");
        }
    
        // Unblocking must not undo causal invalidation. A rejected claim is terminal, so work
        // resting on one is repaired by a replacement item, not by clearing the block.
        if (workItem.DependsOnClaims
            .Select(claimId => state.Claims[claimId])
            .FirstOrDefault(claim => claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded) is { } invalid)
        {
            throw new GovernanceException(
                $"Work item depends on '{invalid.Status.ToString().ToLowerInvariant()}' claim '{invalid.Id}'. " +
                "Add a replacement work item on a current claim instead of unblocking this one.");
        }
    
        return [new WorkItemUnblocked(command.WorkItemId)];
    }

    internal static void EnsureCanStartWork(
        GovernedTaskState state,
        ActorId actorId,
        WorkItem workItem)
    {
        if (workItem.Owner is null || workItem.Owner == actorId || RoleAssignmentRules.IsOperator(state, actorId))
        {
            return;
        }
    
        throw new GovernanceException($"Only work owner '{workItem.Owner}' or an operator can start work item '{workItem.Id}'.");
    }
    
    // A launched agent shares the run's actor identity, so identity cannot distinguish it from the
    // process that launched it. The launcher's secret can. Three live agents closed their own runs
    // despite a briefing telling them not to, which is why this is a rule rather than a sentence.

    private static bool HasOpenRun(GovernedTaskState state, WorkItemId workItemId) =>
        state.Runs.Values.Any(run =>
            run.WorkItemId == workItemId && run.Status is AgentRunStatus.Active);
    
    // Every role that is not judging the work is doing it. Naming the two judging roles rather than
    // listing the working ones means a role added later counts as work by default, which is the
    // safe direction: a new role failing to satisfy this would block completion for no good reason.
    // A null SubjectRole does not count — it predates the field, so the kernel did not see the role
    // and cannot now claim it did.

    private static bool HasCompletedWorkingRun(GovernedTaskState state, WorkItemId workItemId) =>
        state.Runs.Values.Any(run =>
            run.WorkItemId == workItemId &&
            run.Status is AgentRunStatus.Completed &&
            run.SubjectRole is not null and not (RoleKind.Verifier or RoleKind.CodeReviewer));
    
    // The run has to have reached Completed, not merely ended: a verifier whose run failed or was
    // cancelled looked at nothing, and reading the actor's present role instead of the role the run
    // recorded would let a later re-assignment turn any old run into a verification.

    private static bool HasCompletedVerifierRun(GovernedTaskState state, WorkItemId workItemId) =>
        state.Runs.Values.Any(run =>
            run.WorkItemId == workItemId &&
            run.Status is AgentRunStatus.Completed &&
            run.SubjectRole is RoleKind.Verifier);
    
    // The LATEST completed working run, not the earliest. Work done after a verification was not
    // covered by it, so the verifier has to have finished after the last thing it was meant to
    // check. Comparing against the earliest would let an agent verify, keep working, and complete.

    private static AgentRun? LatestCompletedWorkingRun(GovernedTaskState state, WorkItemId workItemId) =>
        state.Runs.Values
            .Where(run => run.WorkItemId == workItemId &&
                          run.Status is AgentRunStatus.Completed &&
                          run.SubjectRole is not null and not (RoleKind.Verifier or RoleKind.CodeReviewer))
            .OrderByDescending(run => run.EndedAt)
            .FirstOrDefault();

    private static DateTimeOffset? LatestCompletedWorkingRunEnd(GovernedTaskState state, WorkItemId workItemId) =>
        LatestCompletedWorkingRun(state, workItemId)?.EndedAt;
    
    // Existence was never the question the gate meant to ask. A verifier run that ended before the
    // work did read an unfinished work item and proves nothing about the finished one. Equal
    // timestamps pass, matching how run completion already compares EndedAt against StartedAt.

    internal static bool HasVerifierRunAfterLatestWork(GovernedTaskState state, WorkItemId workItemId)
    {
        var workedAt = LatestCompletedWorkingRunEnd(state, workItemId);
        return state.Runs.Values.Any(run =>
            run.WorkItemId == workItemId &&
            run.Status is AgentRunStatus.Completed &&
            run.SubjectRole is RoleKind.Verifier &&
            (workedAt is null || run.EndedAt >= workedAt));
    }
    
    // The same question the ordering gate asks, one step further: not only did a verifier see the
    // finished work, but did a verifier who is not the model that wrote it. Returns the provider
    // that did the work when every verifier run qualifying under the ordering gate came from that
    // provider too, and null when at least one came from another.
    //
    // The rule is positional, not a claim that either model verifies better: whoever authored the
    // work is the one who may not also sign it off, and the same two providers swap those places
    // from one work item to the next. Provider, not model or version — a newer build of the same
    // model shares the reasoning that produced the work, which is the thing being guarded against.

    private static string? ProviderThatVerifiedItsOwnWork(GovernedTaskState state, WorkItemId workItemId)
    {
        if (LatestCompletedWorkingRun(state, workItemId) is not { } worked)
        {
            return null;
        }
    
        var verifiedByAnotherProvider = state.Runs.Values.Any(run =>
            run.WorkItemId == workItemId &&
            run.Status is AgentRunStatus.Completed &&
            run.SubjectRole is RoleKind.Verifier &&
            (worked.EndedAt is null || run.EndedAt >= worked.EndedAt) &&
            !string.Equals(run.Provider, worked.Provider, StringComparison.OrdinalIgnoreCase));
    
        return verifiedByAnotherProvider ? null : worked.Provider;
    }

    private static bool HasOpenEscalation(GovernedTaskState state, WorkItemId workItemId) =>
        state.Escalations.Values.Any(escalation =>
            escalation.WorkItemId == workItemId && escalation.Status == EscalationStatus.Open);
}
