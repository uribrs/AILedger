using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Evidences;

internal static class EvidenceRules
{
    internal static IReadOnlyList<LedgerEventData> Add(
        GovernedTaskState state,
        AddEvidenceCommand command,
        DateTimeOffset now)
    {
        RequireId(command.EvidenceId.Value, nameof(command.EvidenceId));
        EnsureNew(state.Evidence, command.EvidenceId, "evidence");
        RequireText(command.SourceType, nameof(command.SourceType));
        RequireText(command.Citation, nameof(command.Citation));
        RequireText(command.Summary, nameof(command.Summary));
        EnsureUnique(command.Supports, "Supported claim IDs");
        EnsureUnique(command.Refutes, "Refuted claim IDs");
        EnsureReferencesExist(state.Claims, command.Supports, "claim");
        EnsureReferencesExist(state.Claims, command.Refutes, "claim");

        EnsureDirectionsDoNotConflict(command.Supports, command.Refutes);

        var evidence = new Evidence(
            command.EvidenceId,
            command.SourceType.Trim(),
            command.Citation.Trim(),
            command.Summary.Trim(),
            command.Supports.ToArray(),
            command.Refutes.ToArray(),
            new Provenance(command.ActorId, now, "evidence.add"));
        return [new EvidenceAdded(evidence)];
    }

    private static void EnsureDirectionsDoNotConflict(
        IReadOnlyList<ClaimId> supports,
        IReadOnlyList<ClaimId> refutes)
    {
        if (supports.Intersect(refutes).Any())
        {
            throw new GovernanceException("The same evidence cannot both support and refute a claim.");
        }
    }
}
