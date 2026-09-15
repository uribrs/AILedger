using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.Artifacts;

internal static class ArtifactScopeRules
{
    internal static void Ensure(GovernedArtifactKind kind, WorkItemId? workItemId)
    {
        var workScoped = kind is GovernedArtifactKind.VerifierOutput or GovernedArtifactKind.CodeReviewOutput;
        if (workScoped != workItemId.HasValue)
        {
            throw new GovernanceException(workScoped
                ? $"A '{kind}' artifact must name its work item."
                : $"A '{kind}' artifact is task-wide and cannot name a work item.");
        }
    }
}
