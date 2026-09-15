using AILedger.Core.Claims;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Challenges;

internal sealed class ClaimChallengeTargetRules : IChallengeTargetRules
{
    public bool Matches(string normalizedTargetType) => normalizedTargetType == "claim";

    public bool Exists(GovernedTaskState state, string targetId) =>
        state.Claims.Keys.Any(id => id.Value == targetId);

    public void AddSupportedConsequence(
        GovernedTaskState state,
        ActorId actorId,
        Challenge challenge,
        ICollection<LedgerEventData> events)
    {
        var claim = Get(state.Claims, new ClaimId(challenge.TargetId.Trim()), "claim");
        if (claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded)
        {
            throw new GovernanceException(
                $"Claim '{claim.Id}' is already '{claim.Status}'; supporting this challenge would change nothing.");
        }

        ChallengeTargetRules.RequireCapability(state, actorId, Capability.ResolveClaim);
        var refutingEvidence = challenge.EvidenceIds
            .Where(id => state.Evidence[id].Refutes.Contains(claim.Id))
            .ToArray();
        if (refutingEvidence.Length != challenge.EvidenceIds.Count || refutingEvidence.Length == 0)
        {
            throw new GovernanceException(
                $"A challenge against claim '{claim.Id}' can only be supported when every piece of its evidence refutes that claim.");
        }

        events.Add(new ClaimResolved(claim.Id, ClaimStatus.Rejected, refutingEvidence));
        ClaimDependencyRules.AddDependencyInvalidations(state, claim.Id, events);
    }
}
