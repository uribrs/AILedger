using AILedger.Core.Contracts;

namespace AILedger.Core.Claims;

// Applies Claim events to the aggregate projection. Command decisions stay in ClaimRules and
// historical-event admissibility stays in ClaimEventValidator; this class only changes state.
internal static class ClaimStateProjector
{
    internal static GovernedTaskState Add(GovernedTaskState state, ClaimAdded added) =>
        state with { Claims = Set(state.Claims, added.Claim.Id, added.Claim) };

    internal static GovernedTaskState Resolve(GovernedTaskState state, ClaimResolved resolved)
    {
        var current = state.Claims[resolved.ClaimId];
        // Evidence accumulates. A challenge rejecting a claim cites only refuting evidence and must
        // not erase the supporting evidence already on record.
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

    internal static GovernedTaskState RepointDependencies(
        GovernedTaskState state,
        ClaimDependenciesRepointed repointed)
    {
        var decisions = state.Decisions;
        // A terminal record's dependency list is the audit trail of what it was actually built on,
        // so a refinement leaves it alone. Correction still stales a completed dependent; refinement
        // deliberately does not touch it.
        foreach (var decision in state.Decisions.Values
                     .Where(item => item.DependsOnClaims.Contains(repointed.SupersededClaimId))
                     .Where(item => item.Status is DecisionStatus.Proposed or DecisionStatus.Accepted))
        {
            decisions = Set(decisions, decision.Id, decision with
            {
                DependsOnClaims = Replace(
                    decision.DependsOnClaims,
                    repointed.SupersededClaimId,
                    repointed.ReplacementClaimId)
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
                DependsOnClaims = Replace(
                    workItem.DependsOnClaims,
                    repointed.SupersededClaimId,
                    repointed.ReplacementClaimId)
            });
        }

        return state with { Decisions = decisions, WorkItems = workItems };
    }

    private static IReadOnlyList<ClaimId> Replace(
        IReadOnlyList<ClaimId> claims,
        ClaimId from,
        ClaimId to) =>
        claims.Select(claimId => claimId == from ? to : claimId).Distinct().ToArray();

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
