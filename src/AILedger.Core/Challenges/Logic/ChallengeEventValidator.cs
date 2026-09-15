using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.Challenges;

// Replay checks historical Challenge event shapes independently from today's command policy.
internal static class ChallengeEventValidator
{
    internal static void ValidateRaised(
        GovernedTaskState state,
        LedgerEvent @event,
        Challenge challenge)
    {
        RequireAuthority(state, @event.ActorId, Capability.RaiseChallenge);
        EnsureNew(state.Challenges, challenge.Id, "challenge");
        RequireId(challenge.Id.Value, nameof(challenge.Id));
        RequireText(challenge.TargetType, nameof(challenge.TargetType));
        RequireText(challenge.TargetId, nameof(challenge.TargetId));
        RequireText(challenge.Reason, nameof(challenge.Reason));
        RequireDefined(challenge.Status, nameof(challenge.Status));
        if (challenge.Status != ChallengeStatus.Open)
        {
            throw new GovernanceException("A newly raised challenge must be open.");
        }

        EnsureUnique(challenge.EvidenceIds, "Evidence IDs");
        EnsureReferencesExist(state.Evidence, challenge.EvidenceIds, "evidence");
        ChallengeTargetRules.EnsureExists(state, challenge.TargetType, challenge.TargetId);
        ValidateProvenance(@event, challenge.Provenance, "challenge.raise");
    }

    internal static void ValidateDisposed(
        GovernedTaskState state,
        LedgerEvent @event,
        ChallengeDisposed disposed)
    {
        RequireAuthority(state, @event.ActorId, Capability.DisposeChallenge);
        RequireDefined(disposed.Status, nameof(disposed.Status));
        var challenge = Get(state.Challenges, disposed.ChallengeId, "challenge");
        if (challenge.Status != ChallengeStatus.Open || disposed.Status == ChallengeStatus.Open)
        {
            throw new GovernanceException(
                "Only an open challenge can transition to a terminal disposition.");
        }
    }
}
