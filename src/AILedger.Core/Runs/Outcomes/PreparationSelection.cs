using AILedger.Core.Domain;
namespace AILedger.Core.Contracts;

// References to admitted task truth; profile identifiers never contain commands or grants.
public sealed record PreparationSelection(DecisionId Decision, ArtifactId Plan, ArtifactId Request,
    IReadOnlyList<string> Profiles, IReadOnlyList<EvidenceId> Evidence,
    IReadOnlyList<ConstraintId>? ConstraintBasis = null, IReadOnlyList<EvidenceId>? DependencyEvidenceBasis = null);
public sealed record SelectPreparationCommand(ActorId ActorId, EventId? CausationId, string CorrelationId,
    RunId RunId, PreparationSelection Selection) : LedgerCommand(ActorId, CausationId, CorrelationId);
public sealed record PreparationSelected(RunId RunId, PreparationSelection Selection) : LedgerEventData;

public static class PreparationSelectionRules
{
    public static void EnsureProducerCurrent(GovernedTaskState state, AgentRun run)
    {
        if (run.Status != AgentRunStatus.Completed || run.SubjectRole != RoleKind.PlanningLead ||
            !state.Roles.TryGetValue(run.ActorId, out var producer) || producer.Role != RoleKind.PlanningLead ||
            producer.AssignedBy.RecordedAt > run.StartedAt || !producer.Capabilities.Contains(Capability.ResolveDecision))
            throw new GovernanceException("Planning selection producer authority changed; obtain a fresh authorized selection.");
    }

    public static void EnsureApplicable(GovernedTaskState state, PreparationSelection selection)
    {
        if (!state.Decisions.TryGetValue(selection.Decision, out var decision) || decision.Status != DecisionStatus.Accepted ||
            decision.DependsOnClaims.Count == 0 || decision.DependsOnClaims.Any(id => !state.Claims.TryGetValue(id, out var claim) || claim.Status != ClaimStatus.Validated || claim.SupersededByClaimId is not null))
            throw new GovernanceException("Preparation requires an accepted decision with validated current dependencies.");
        if (selection.ConstraintBasis is not null && !selection.ConstraintBasis.ToHashSet().SetEquals(state.Constraints.Values.Where(c => c.Status == ConstraintStatus.Active).Select(c => c.Id)) ||
            selection.DependencyEvidenceBasis is not null && !selection.DependencyEvidenceBasis.ToHashSet().SetEquals(
                state.Evidence.Values.Where(e => e.Supports.Concat(e.Refutes).Intersect(decision.DependsOnClaims).Any()).Select(e => e.Id)))
            throw new GovernanceException("Preparation's constraint or dependency-evidence basis changed; reassessment is required.");
        var artifacts = ArtifactApplicability.Current(state).ToDictionary(a => a.ArtifactId);
        if (!artifacts.TryGetValue(selection.Plan, out var plan) || plan.Kind != GovernedArtifactKind.OrchestrationPlan ||
            !artifacts.TryGetValue(selection.Request, out var request) || request.Kind != GovernedArtifactKind.UserRequest)
            throw new GovernanceException("Preparation requires the current plan and trusted request.");
        if (selection.Profiles is not { Count: >= 1 and <= 16 } || selection.Profiles.Distinct().Count() != selection.Profiles.Count ||
            selection.Profiles.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 128) ||
            selection.Evidence is not { Count: >= 1 and <= 16 } || selection.Evidence.Any(id => !state.Evidence.ContainsKey(id)))
            throw new GovernanceException("Preparation requires bounded profile selections and actual evidence.");
        if (state.Escalations.Values.Any(e => e.Status == EscalationStatus.Open) ||
            state.Challenges.Values.Any(c => c.Status == ChallengeStatus.Open))
            throw new GovernanceException("Unresolved escalation or challenge prevents preparation.");
    }

    internal static void Validate(GovernedTaskState state, ActorId actor, RunId runId, PreparationSelection selection)
    {
        if (!state.Runs.TryGetValue(runId, out var run) || run.Status != AgentRunStatus.Active || run.ActorId != actor ||
            run.SubjectRole != RoleKind.PlanningLead || state.Stage != TaskStage.Scope || run.PreparationSelection is not null)
            throw new GovernanceException("Preparation selection requires its active Scope planning lead and is immutable.");
        EnsureApplicable(state, selection);
        if (state.Artifacts[selection.Plan].ProducerRunId != runId ||
            selection.Evidence.Any(id => state.Evidence[id].Provenance.ActorId != actor || state.Evidence[id].Provenance.RecordedAt < run.StartedAt))
            throw new GovernanceException("Selection requires the producer's current plan and evidence.");
    }

    internal static IReadOnlyList<LedgerEventData> Record(GovernedTaskState state, SelectPreparationCommand command)
    {
        Validate(state, command.ActorId, command.RunId, command.Selection);
        return [new PreparationSelected(command.RunId, command.Selection with
            { Profiles = command.Selection.Profiles.ToArray(), Evidence = command.Selection.Evidence.ToArray(),
              ConstraintBasis = state.Constraints.Values.Where(c => c.Status == ConstraintStatus.Active).Select(c => c.Id).ToArray(), DependencyEvidenceBasis = state.Evidence.Values
                .Where(e => e.Supports.Concat(e.Refutes).Intersect(state.Decisions[command.Selection.Decision].DependsOnClaims).Any()).Select(e => e.Id).ToArray() })];
    }
}
