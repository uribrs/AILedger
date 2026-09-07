using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Replay counterparts: ValidateEscalationRaised and ValidateEscalationResolved.
internal static class EscalationRules
{
    internal static IReadOnlyList<LedgerEventData> RaiseEscalation(
        GovernedTaskState state,
        RaiseEscalationCommand command,
        DateTimeOffset now)
    {
        RequireId(command.EscalationId.Value, nameof(command.EscalationId));
        EnsureNew(state.Escalations, command.EscalationId, "escalation");
        RequireText(command.Question, nameof(command.Question));
    
        if (command.WorkItemId is { } workItemId)
        {
            _ = Get(state.WorkItems, workItemId, "work item");
        }
    
        var options = command.Options.Select(option => option.Trim()).ToArray();
        var recommendation = TrimOrNull(command.Recommendation);
        if (options.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("An escalation cannot carry an empty option.");
        }
    
        EnsureUnique(options, "Options", StringComparer.Ordinal);
        EnsureUnique(command.AttemptEvidenceIds, "Attempt evidence IDs");
        EnsureReferencesExist(state.Evidence, command.AttemptEvidenceIds, "evidence");
    
        // The two kinds exist to keep everything else off the operator's desk, so each
        // one has to carry the payload that proves it earned the interruption.
        switch (command.Kind)
        {
            case EscalationKind.BusinessDecision:
                if (options.Length < 2)
                {
                    throw new GovernanceException(
                        "A business decision requires at least two options for the operator to choose between.");
                }
    
                if (recommendation is null)
                {
                    throw new GovernanceException("A business decision requires a recommendation.");
                }
    
                if (!options.Contains(recommendation, StringComparer.Ordinal))
                {
                    throw new GovernanceException("A business decision recommendation must name one of its options.");
                }
    
                break;
            case EscalationKind.TrueUnknown:
                if (command.AttemptEvidenceIds.Count == 0)
                {
                    throw new GovernanceException(
                        "A true unknown requires evidence of the attempt that failed to answer it.");
                }
    
                break;
            default:
                throw new GovernanceException($"Unsupported escalation kind '{command.Kind}'.");
        }
    
        var escalation = new Escalation(
            command.EscalationId,
            command.Kind,
            command.Question.Trim(),
            EscalationStatus.Open,
            command.WorkItemId,
            options,
            recommendation,
            command.AttemptEvidenceIds.ToArray(),
            null,
            null,
            new Provenance(command.ActorId, now, "escalation.raise"));
        return [new EscalationRaised(escalation)];
    }

    internal static IReadOnlyList<LedgerEventData> ResolveEscalation(
        GovernedTaskState state,
        ResolveEscalationCommand command)
    {
        var escalation = Get(state.Escalations, command.EscalationId, "escalation");
        if (escalation.Status != EscalationStatus.Open)
        {
            throw new GovernanceException("Only an open escalation can be resolved.");
        }
    
        if (command.Status == EscalationStatus.Open)
        {
            throw new GovernanceException("Escalation resolution must be terminal.");
        }
    
        var resolution = TrimOrNull(command.Resolution);
        if (command.Status == EscalationStatus.Resolved && resolution is null)
        {
            throw new GovernanceException("Resolving an escalation requires the operator's answer.");
        }
    
        return [new EscalationResolved(command.EscalationId, command.Status, resolution, command.ActorId)];
    }
}
