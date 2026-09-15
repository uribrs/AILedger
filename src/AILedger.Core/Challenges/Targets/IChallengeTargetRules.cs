using AILedger.Core.Contracts;

namespace AILedger.Core.Challenges;

internal interface IChallengeTargetRules
{
    bool Matches(string normalizedTargetType);

    bool Exists(GovernedTaskState state, string targetId);

    void AddSupportedConsequence(
        GovernedTaskState state,
        ActorId actorId,
        Challenge challenge,
        ICollection<LedgerEventData> events);
}
