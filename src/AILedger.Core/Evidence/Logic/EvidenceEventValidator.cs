using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.Evidences;

// Replay validates the historical event shape independently from today's command rules.
internal static class EvidenceEventValidator
{
    internal static void ValidateAdded(
        GovernedTaskState state,
        LedgerEvent @event,
        Evidence evidence)
    {
        RequireAuthority(state, @event.ActorId, Capability.AddEvidence);
        EnsureNew(state.Evidence, evidence.Id, "evidence");
        RequireId(evidence.Id.Value, nameof(evidence.Id));
        RequireText(evidence.SourceType, nameof(evidence.SourceType));
        RequireText(evidence.Citation, nameof(evidence.Citation));
        RequireText(evidence.Summary, nameof(evidence.Summary));
        EnsureUnique(evidence.Supports, "Supported claim IDs");
        EnsureUnique(evidence.Refutes, "Refuted claim IDs");
        EnsureReferencesExist(state.Claims, evidence.Supports, "claim");
        EnsureReferencesExist(state.Claims, evidence.Refutes, "claim");
        if (evidence.Supports.Intersect(evidence.Refutes).Any())
        {
            throw new GovernanceException("The same evidence cannot both support and refute a claim.");
        }

        if (evidence.TrustedTerminationVersion is { } version &&
            (version != 1 || evidence.SourceType != "process-termination" || !IsOperator(state, @event.ActorId)))
            throw new GovernanceException("Trusted termination attribution requires schema 1 and an operator at recording time.");
        // Older legal evidence has no attribution field and remains replayable.
        ValidateProvenance(@event, evidence.Provenance, "evidence.add");
    }
}
