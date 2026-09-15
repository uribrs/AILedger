using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Challenges;

internal sealed class DecisionChallengeTargetRules : IChallengeTargetRules
{
    public bool Matches(string normalizedTargetType) => normalizedTargetType == "decision";

    public bool Exists(GovernedTaskState state, string targetId) =>
        state.Decisions.Keys.Any(id => id.Value == targetId);

    public void AddSupportedConsequence(
        GovernedTaskState state,
        ActorId actorId,
        Challenge challenge,
        ICollection<LedgerEventData> events)
    {
        var decision = Get(state.Decisions, new DecisionId(challenge.TargetId.Trim()), "decision");
        if (decision.Status is not (DecisionStatus.Proposed or DecisionStatus.Accepted))
        {
            throw new GovernanceException(
                $"Decision '{decision.Id}' is already '{decision.Status}'; supporting this challenge would change nothing.");
        }

        ChallengeTargetRules.RequireCapability(state, actorId, Capability.ResolveDecision);
        events.Add(new DecisionOverturned(decision.Id, challenge.Id));
    }
}
