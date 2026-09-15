using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Artifacts;

internal static class VerifierOutputRules
{
    internal static void Validate(GovernedTaskState state, WorkItemId workItemId, string content)
    {
        var workItem = Get(state.WorkItems, workItemId, "work item");
        var assumptions = MarkdownTableReader.Read(content, ["id", "status", "name", "citation", "actor"]);
        MarkdownTableReader.EnsureUnique(
            assumptions.Select(row => row[0]).ToArray(),
            "Verifier assumption disposition IDs",
            StringComparer.Ordinal);
        foreach (var claimId in workItem.DependsOnClaims.Where(id =>
                     state.Claims[id].Status is ClaimStatus.Open or ClaimStatus.Validated))
        {
            var row = assumptions.SingleOrDefault(candidate => candidate[0] == claimId.Value)
                ?? throw new GovernanceException($"Verifier output must dispose dependent claim '{claimId}'.");
            if (row[1] is not ("VALIDATED" or "REJECTED" or "NEVER-TESTED") ||
                string.IsNullOrWhiteSpace(row[2]) ||
                string.IsNullOrWhiteSpace(row[3]) ||
                string.IsNullOrWhiteSpace(row[4]))
            {
                throw new GovernanceException(
                    $"Verifier disposition for claim '{claimId}' must be terminal and carry a name, citation, and actor.");
            }
        }

        var plan = ArtifactRevisionRules.Current(state).SingleOrDefault(item =>
            item.Kind == GovernedArtifactKind.OrchestrationPlan);
        if (plan is null)
        {
            throw new GovernanceException("A verifier output requires a current orchestration plan.");
        }

        var attentionIds = plan.Content.Contains("No material attention items", StringComparison.OrdinalIgnoreCase)
            ? Array.Empty<string>()
            : MarkdownTableReader.Read(
                    plan.Content,
                    ["id", "name", "failure mode", "causal path and impact", "planned handling", "source"])
                .Select(row => row[0])
                .Where(IsAttentionId)
                .ToArray();
        MarkdownTableReader.EnsureUnique(
            attentionIds,
            "Orchestration-plan attention item IDs",
            StringComparer.Ordinal);

        var dispositions = MarkdownTableReader.Read(
            content,
            ["id", "final disposition", "name", "evidence"]);
        MarkdownTableReader.EnsureUnique(
            dispositions.Select(row => row[0]).ToArray(),
            "Verifier attention disposition IDs",
            StringComparer.Ordinal);
        foreach (var attentionId in attentionIds)
        {
            var row = dispositions.SingleOrDefault(candidate => candidate[0] == attentionId)
                ?? throw new GovernanceException($"Verifier output must dispose attention item '{attentionId}'.");
            if (row[1] is not ("handled" or "accepted-risk" or "not-applicable" or "unresolved") ||
                string.IsNullOrWhiteSpace(row[2]) ||
                string.IsNullOrWhiteSpace(row[3]))
            {
                throw new GovernanceException(
                    $"Verifier attention disposition '{attentionId}' must use an allowed value and carry a name and evidence.");
            }
        }
    }

    private static bool IsAttentionId(string value)
    {
        if (value.Length < 2 || value[0] != 'R')
        {
            return false;
        }

        var digitCount = value.Skip(1).TakeWhile(char.IsDigit).Count();
        return digitCount > 0 && (digitCount == value.Length - 1 ||
            digitCount == value.Length - 2 && char.IsLetter(value[^1]));
    }
}
