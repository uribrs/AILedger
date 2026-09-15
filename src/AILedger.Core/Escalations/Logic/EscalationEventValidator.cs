using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.Escalations;

// Replay validates the event shape that command-time EscalationRules emits. The checks deliberately
// remain separate because replay must continue accepting every history that was legal.
internal static class EscalationEventValidator
{
    internal static void ValidateRaised(
        GovernedTaskState state,
        LedgerEvent @event,
        Escalation escalation)
    {
        RequireAuthority(state, @event.ActorId, Capability.RaiseEscalation);
        EnsureNew(state.Escalations, escalation.Id, "escalation");
        RequireId(escalation.Id.Value, nameof(escalation.Id));
        RequireText(escalation.Question, nameof(escalation.Question));
        RequireDefined(escalation.Kind, nameof(escalation.Kind));
        RequireDefined(escalation.Status, nameof(escalation.Status));
        if (escalation.Status != EscalationStatus.Open)
        {
            throw new GovernanceException("A newly raised escalation must be open.");
        }

        if (escalation.Resolution is not null || escalation.ResolvedBy is not null)
        {
            throw new GovernanceException("A newly raised escalation cannot carry a resolution.");
        }

        if (escalation.WorkItemId is { } workItemId)
        {
            _ = Get(state.WorkItems, workItemId, "work item");
        }

        EnsureUnique(escalation.AttemptEvidenceIds, "Attempt evidence IDs");
        EnsureReferencesExist(state.Evidence, escalation.AttemptEvidenceIds, "evidence");
        if (escalation.Options.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("An escalation cannot carry an empty option.");
        }

        EnsureUnique(escalation.Options, "Options", StringComparer.Ordinal);

        switch (escalation.Kind)
        {
            case EscalationKind.BusinessDecision:
                if (escalation.Options.Count < 2)
                {
                    throw new GovernanceException(
                        "A business decision requires at least two options for the operator to choose between.");
                }

                if (string.IsNullOrWhiteSpace(escalation.Recommendation))
                {
                    throw new GovernanceException("A business decision requires a recommendation.");
                }

                if (!escalation.Options.Contains(escalation.Recommendation, StringComparer.Ordinal))
                {
                    throw new GovernanceException("A business decision recommendation must name one of its options.");
                }

                break;
            case EscalationKind.TrueUnknown:
                if (escalation.AttemptEvidenceIds.Count == 0)
                {
                    throw new GovernanceException(
                        "A true unknown requires evidence of the attempt that failed to answer it.");
                }

                break;
            default:
                throw new GovernanceException($"Unsupported escalation kind '{escalation.Kind}'.");
        }

        ValidateProvenance(@event, escalation.Provenance, "escalation.raise");
    }

    internal static void ValidateResolved(
        GovernedTaskState state,
        LedgerEvent @event,
        EscalationResolved resolved)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveEscalation, operatorRequired: true);
        RequireDefined(resolved.Status, nameof(resolved.Status));
        var escalation = Get(state.Escalations, resolved.EscalationId, "escalation");
        if (escalation.Status != EscalationStatus.Open || resolved.Status == EscalationStatus.Open)
        {
            throw new GovernanceException("Only an open escalation can transition to a terminal disposition.");
        }

        if (resolved.Status == EscalationStatus.Resolved && string.IsNullOrWhiteSpace(resolved.Resolution))
        {
            throw new GovernanceException("Resolving an escalation requires the operator's answer.");
        }

        if (resolved.ResolvedBy != @event.ActorId)
        {
            throw new GovernanceException("An escalation must record the actor that resolved it.");
        }
    }
}
