using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Replay counterparts: ValidateDecisionInvalidated, ValidateWorkItemInvalidated, ValidateClaimDependenciesRepointed.
internal static class ClaimDependencyRules
{
    internal static void AddDependencyInvalidations(
        GovernedTaskState state,
        ClaimId claimId,
        ICollection<LedgerEventData> events)
    {
        foreach (var decision in state.Decisions.Values
                     .Where(item => item.DependsOnClaims.Contains(claimId))
                     .Where(item => item.Status is DecisionStatus.Proposed or DecisionStatus.Accepted)
                     .OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            events.Add(new DecisionInvalidated(decision.Id, claimId));
        }
    
        foreach (var workItem in state.WorkItems.Values
                     .Where(item => item.DependsOnClaims.Contains(claimId))
                     // R3 (workitem-blocked-conflation): an operator `work block` sets the same
                     // Blocked member as invalidation does. Skipping Blocked here would mean a
                     // manually blocked item never goes Stale when its claim is later rejected.
                     .Where(item => item.Status is not WorkItemStatus.Stale)
                     .OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            var status = workItem.Status == WorkItemStatus.Active
                ? WorkItemStatus.Blocked
                : WorkItemStatus.Stale;
            events.Add(new WorkItemInvalidated(workItem.Id, claimId, status));
        }
    }
    
    // Disjoint work items were only ever disjoint by assertion. An area is occupied while the work
    // item holding it is still live, so a second actor cannot claim it and redo the same work.

    internal static void EnsureDependenciesAreCurrent(
        GovernedTaskState state,
        IEnumerable<ClaimId> claimIds)
    {
        var invalid = claimIds
            .Select(claimId => state.Claims[claimId])
            .FirstOrDefault(claim => claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded);
        if (invalid is not null)
        {
            throw new GovernanceException(
                $"Dependency claim '{invalid.Id}' is '{invalid.Status}' and cannot support new or actionable work.");
        }
    }
}
