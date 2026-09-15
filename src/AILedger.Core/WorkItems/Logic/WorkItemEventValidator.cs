using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.WorkItems;

// Replay validates the event shapes emitted by WorkItemLifecycleRules. It deliberately omits newer
// coordination gates when applying them retroactively would reject histories that were legal.
internal static class WorkItemEventValidator
{
    internal static void ValidateAdded(
        GovernedTaskState state,
        LedgerEvent @event,
        WorkItem workItem)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageWork, operatorRequired: true);
        RequireAuthority(state, @event.ActorId, Capability.ManageScope, operatorRequired: true);
        EnsureNew(state.WorkItems, workItem.Id, "work item");
        RequireId(workItem.Id.Value, nameof(workItem.Id));
        RequireText(workItem.Title, nameof(workItem.Title));
        RequireDefined(workItem.Status, nameof(workItem.Status));
        if (workItem.Status != WorkItemStatus.Proposed)
        {
            throw new GovernanceException("A newly added work item must have proposed status.");
        }

        EnsureUnique(workItem.DependsOnClaims, "Dependent claim IDs");
        EnsureReferencesExist(state.Claims, workItem.DependsOnClaims, "claim");
        EnsureDependenciesAreCurrent(state, workItem.DependsOnClaims);
        EnsureUnique(workItem.ResourceScope, "Resource scope entries", StringComparer.Ordinal);
        if (workItem.ResourceScope.Any(string.IsNullOrWhiteSpace) ||
            workItem.ResourceScope.Any(scope => !Path.IsPathFullyQualified(scope)))
        {
            throw new GovernanceException("Resource scope entries must be non-empty absolute paths.");
        }

        // Occupancy and the absence of a multi-scope justification are command-time rules. Older logs
        // legally contain overlaps and multi-area items without the later field. A present field is safe
        // to validate because no pre-field event can contain it.
        if (workItem.NotSplitJustification is { } justificationId)
        {
            _ = Get(state.Alternatives, justificationId, "alternative");
        }

        if (workItem.Owner is { } owner && !state.Roles.ContainsKey(owner))
        {
            throw new GovernanceException($"Work owner '{owner}' has no assigned role.");
        }
    }

    internal static void ValidateInvalidated(
        GovernedTaskState state,
        LedgerEvent @event,
        WorkItemInvalidated invalidated)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveClaim);
        RequireDefined(invalidated.Status, nameof(invalidated.Status));
        var workItem = Get(state.WorkItems, invalidated.WorkItemId, "work item");
        if (workItem.Status is WorkItemStatus.Stale)
        {
            throw new GovernanceException("An already invalidated work item cannot be invalidated again.");
        }

        var expectedStatus = workItem.Status == WorkItemStatus.Active
            ? WorkItemStatus.Blocked
            : WorkItemStatus.Stale;
        if (invalidated.Status != expectedStatus)
        {
            throw new GovernanceException($"Work item invalidation must transition to '{expectedStatus}'.");
        }

        var claim = Get(state.Claims, invalidated.RejectedClaimId, "claim");
        if (claim.Status is not (ClaimStatus.Rejected or ClaimStatus.Superseded) ||
            !workItem.DependsOnClaims.Contains(claim.Id))
        {
            throw new GovernanceException(
                "A work item can only be invalidated by one of its rejected or superseded claims.");
        }
    }

    internal static void ValidateCompleted(
        GovernedTaskState state,
        LedgerEvent @event,
        WorkItemCompleted completed)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageWork);
        var workItem = Get(state.WorkItems, completed.WorkItemId, "work item");
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

        if (state.Runs.Values.Any(run =>
                run.WorkItemId == completed.WorkItemId && run.Status is AgentRunStatus.Active))
        {
            throw new GovernanceException("Work item cannot be completed while a run is still active.");
        }

        if (state.Escalations.Values.Any(escalation =>
                escalation.WorkItemId == completed.WorkItemId && escalation.Status == EscalationStatus.Open))
        {
            throw new GovernanceException("Work item cannot be completed while an escalation on it is open.");
        }

        // Required runs and their ordering remain command-time only: old histories have neither the
        // subject-role data nor the obligations needed to prove them. A present waiver is a newer shape
        // and can safely be checked for content and operator authority.
        if (completed.WithoutVerificationReason is { } waiver)
        {
            if (string.IsNullOrWhiteSpace(waiver))
            {
                throw new GovernanceException(
                    "Waiving the required runs needs a reason; a blank waiver records nothing.");
            }

            if (!IsOperator(state, @event.ActorId))
            {
                throw new GovernanceException("Only an operator can complete work without the required runs.");
            }
        }
    }

    internal static void ValidateBlocked(
        GovernedTaskState state,
        LedgerEvent @event,
        WorkItemBlocked blocked)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageWork);
        var workItem = Get(state.WorkItems, blocked.WorkItemId, "work item");
        RequireText(blocked.Reason, nameof(blocked.Reason));
        // Stale stays legal at replay because blocking a stale item was historically accepted. Abandoned
        // is safe to reject because the status and event were introduced together.
        if (workItem.Status is WorkItemStatus.Completed or WorkItemStatus.Abandoned)
        {
            throw new GovernanceException("A completed or abandoned work item cannot be blocked.");
        }

        if (blocked.EscalationId is { } escalationId)
        {
            var escalation = Get(state.Escalations, escalationId, "escalation");
            if (escalation.Status != EscalationStatus.Open)
            {
                throw new GovernanceException("A work item can only be blocked on an open escalation.");
            }
        }
    }

    internal static void ValidateUnblocked(
        GovernedTaskState state,
        LedgerEvent @event,
        WorkItemUnblocked unblocked)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageWork);
        var workItem = Get(state.WorkItems, unblocked.WorkItemId, "work item");
        if (workItem.Status != WorkItemStatus.Blocked)
        {
            throw new GovernanceException(
                $"Only a blocked work item can be unblocked; this one is '{workItem.Status}'.");
        }

        if (workItem.DependsOnClaims
            .Select(claimId => Get(state.Claims, claimId, "claim"))
            .Any(claim => claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded))
        {
            throw new GovernanceException(
                "A work item depending on a rejected or superseded claim cannot be unblocked.");
        }
    }

    internal static void ValidateAbandoned(
        GovernedTaskState state,
        LedgerEvent @event,
        WorkItemAbandoned abandoned)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageWork, operatorRequired: true);
        var workItem = Get(state.WorkItems, abandoned.WorkItemId, "work item");
        RequireText(abandoned.Reason, nameof(abandoned.Reason));
        if (workItem.Status is WorkItemStatus.Completed or WorkItemStatus.Stale)
        {
            throw new GovernanceException("A completed or stale work item cannot be abandoned.");
        }

        if (workItem.Status is WorkItemStatus.Abandoned)
        {
            throw new GovernanceException("Work item is already abandoned.");
        }

        // Active-run and open-escalation checks remain command-time coordination policy. Replay keeps
        // this validator focused on the shape and admissible state transition of the recorded event.
    }

    private static void EnsureDependenciesAreCurrent(
        GovernedTaskState state,
        IEnumerable<ClaimId> claimIds)
    {
        var invalid = claimIds
            .Select(claimId => state.Claims[claimId])
            .FirstOrDefault(claim => claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded);
        if (invalid is not null)
        {
            throw new GovernanceException(
                $"Dependency claim '{invalid.Id}' is '{invalid.Status}' and cannot support actionable work.");
        }
    }
}
