using AILedger.Core.Domain;

namespace AILedger.Core.Contracts;

/// <summary>Interprets legacy scope and explicit symmetric assurance membership.</summary>
public static class WorkCoverage
{
    public static IReadOnlyList<WorkItemId> Effective(
        WorkItemId? workItemId, AssuranceBinding? assurance) =>
        assurance is null
            ? Normalize(workItemId, null)
            : Normalize(workItemId, assurance.WorkItemIds
                ?? throw new GovernanceException("Assurance coverage: member list is required."));

    public static IReadOnlyList<WorkItemId> Normalize(
        WorkItemId? workItemId, IReadOnlyList<WorkItemId>? coveredWorkItemIds)
    {
        // Historical singular IDs keep their original meaning; only explicit lists gain new rules.
        if (coveredWorkItemIds is null)
        {
            return workItemId is { } legacyId ? [legacyId] : [];
        }

        if (workItemId is null)
        {
            throw new GovernanceException("Assurance coverage: an explicit member list requires a work item anchor.");
        }

        if (coveredWorkItemIds.Count == 0)
        {
            throw new GovernanceException("Assurance coverage: member list must not be empty.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in coveredWorkItemIds)
        {
            if (string.IsNullOrWhiteSpace(member.Value) || member.Value.Any(char.IsWhiteSpace))
            {
                throw new GovernanceException("Assurance coverage: member IDs must be nonblank and contain no whitespace.");
            }

            if (!ids.Add(member.Value))
            {
                throw new GovernanceException($"Assurance coverage: duplicate member '{member}'.");
            }
        }

        if (!ids.Contains(workItemId.Value.Value))
        {
            throw new GovernanceException($"Assurance coverage: member list omits anchor '{workItemId}'.");
        }

        return coveredWorkItemIds.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
    }
}
