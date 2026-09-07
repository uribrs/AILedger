using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Replay counterpart: TaskTransitionValidator.ValidateEvidenceAdded.
internal static class EvidenceRules
{
    internal static IReadOnlyList<LedgerEventData> AddEvidence(
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
    
        if (command.Supports.Intersect(command.Refutes).Any())
        {
            throw new GovernanceException("The same evidence cannot both support and refute a claim.");
        }
    
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
}
