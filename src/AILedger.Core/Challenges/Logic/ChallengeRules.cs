using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Challenges;

// Challenge lifecycle coordinates target policies; each target owns its consequence rules.
internal static class ChallengeRules
{
    internal static IReadOnlyList<LedgerEventData> RaiseChallenge(
        GovernedTaskState state,
        RaiseChallengeCommand command,
        DateTimeOffset now)
    {
        RequireId(command.ChallengeId.Value, nameof(command.ChallengeId));
        EnsureNew(state.Challenges, command.ChallengeId, "challenge");
        RequireText(command.TargetType, nameof(command.TargetType));
        RequireText(command.TargetId, nameof(command.TargetId));
        RequireText(command.Reason, nameof(command.Reason));
        EnsureUnique(command.EvidenceIds, "Evidence IDs");
        EnsureReferencesExist(state.Evidence, command.EvidenceIds, "evidence");
        ChallengeTargetRules.EnsureExists(state, command.TargetType, command.TargetId);

        var challenge = new Challenge(
            command.ChallengeId,
            command.TargetType.Trim(),
            command.TargetId.Trim(),
            command.Reason.Trim(),
            ChallengeStatus.Open,
            command.EvidenceIds.ToArray(),
            new Provenance(command.ActorId, now, "challenge.raise"));
        return [new ChallengeRaised(challenge)];
    }

    internal static IReadOnlyList<LedgerEventData> DisposeChallenge(
        GovernedTaskState state,
        DisposeChallengeCommand command)
    {
        var challenge = Get(state.Challenges, command.ChallengeId, "challenge");
        if (challenge.Status != ChallengeStatus.Open)
        {
            throw new GovernanceException("Only an open challenge can be disposed.");
        }

        if (command.Status == ChallengeStatus.Open)
        {
            throw new GovernanceException("Challenge disposition must be terminal.");
        }

        var events = new List<LedgerEventData>
        {
            new ChallengeDisposed(command.ChallengeId, command.Status)
        };

        if (command.Status == ChallengeStatus.Supported)
        {
            ChallengeTargetRules.AddSupportedConsequence(state, command.ActorId, challenge, events);
        }

        return events;
    }
}
