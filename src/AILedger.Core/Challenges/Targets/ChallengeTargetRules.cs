using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.Challenges;

// This explicit registry is the extension seam for persisted challenge target types. Keeping the
// accepted vocabulary visible is intentional: aliases are part of replay compatibility.
internal static class ChallengeTargetRules
{
    private static readonly IReadOnlyList<IChallengeTargetRules> Targets =
    [
        new ClaimChallengeTargetRules(),
        new DecisionChallengeTargetRules(),
        new WorkItemChallengeTargetRules()
    ];

    internal static void EnsureExists(GovernedTaskState state, string targetType, string targetId)
    {
        var target = Resolve(targetType);
        if (!target.Exists(state, targetId.Trim()))
        {
            throw new GovernanceException($"Challenge target '{targetType}:{targetId}' does not exist.");
        }
    }

    internal static void AddSupportedConsequence(
        GovernedTaskState state,
        ActorId actorId,
        Challenge challenge,
        ICollection<LedgerEventData> events)
    {
        if (challenge.EvidenceIds.Count == 0)
        {
            throw new GovernanceException(
                $"Challenge '{challenge.Id}' carries no evidence and cannot be supported.");
        }

        Resolve(challenge.TargetType).AddSupportedConsequence(state, actorId, challenge, events);
    }

    internal static void RequireCapability(
        GovernedTaskState state,
        ActorId actorId,
        Capability capability)
    {
        if (!state.Roles[actorId].Capabilities.Contains(capability))
        {
            throw new GovernanceException(
                $"Supporting this challenge emits a '{capability}' transition, which actor '{actorId}' lacks.");
        }
    }

    private static IChallengeTargetRules Resolve(string targetType)
    {
        var normalized = targetType.Trim().ToLowerInvariant();
        return Targets.FirstOrDefault(target => target.Matches(normalized))
            ?? throw new GovernanceException($"Unsupported challenge target type '{targetType}'.");
    }
}
