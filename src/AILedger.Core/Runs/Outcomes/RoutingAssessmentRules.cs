using AILedger.Core.Contracts;
using AILedger.Core.Domain;
namespace AILedger.Core.Runs;

internal static class RoutingAssessmentRules
{
    internal static IReadOnlyList<LedgerEventData> Record(GovernedTaskState state, RecordRoutingAssessmentCommand command)
    {
        Validate(state, command.ActorId, command.RunId, command.Assessment);
        return [new RoutingAssessmentRecorded(command.RunId, command.Assessment with
            { EvidenceIds = command.Assessment.EvidenceIds.ToArray() })];
    }

    internal static void Validate(GovernedTaskState state, ActorId actor, RunId id, RoutingAssessment assessment)
    {
        if (!state.Runs.TryGetValue(id, out var run) || run.Status != AgentRunStatus.Active ||
            run.ActorId != actor || run.RoutingAssessment is not null ||
            !state.Roles.TryGetValue(actor, out var assignment) || assignment.Role != run.SubjectRole ||
            !assignment.Capabilities.Contains(Capability.AddEvidence))
            throw new GovernanceException("A routing assessment requires its owning active run and AddEvidence; it is immutable.");
        if (assessment is null || !Enum.IsDefined(assessment.Work) || !Enum.IsDefined(assessment.Disposition) ||
            string.IsNullOrWhiteSpace(assessment.Rationale) || assessment.Rationale.Length > 8192 ||
            assessment.EvidenceIds is not { Count: >= 1 and <= 16 } ||
            assessment.EvidenceIds.Distinct().Count() != assessment.EvidenceIds.Count)
            throw new GovernanceException("Routing judgment requires defined work/disposition, rationale and 1–16 unique evidence references.");
        var allowed = assessment.Work switch
        {
            CognitiveWorkKind.Implementation or CognitiveWorkKind.Repair => run.SubjectRole == RoleKind.Worker && run.WorkItemId is not null,
            CognitiveWorkKind.Verification => run.SubjectRole == RoleKind.Verifier && run.WorkItemId is not null,
            CognitiveWorkKind.Review => run.SubjectRole == RoleKind.CodeReviewer && run.WorkItemId is not null,
            CognitiveWorkKind.Research => run.SubjectRole == RoleKind.Researcher,
            _ => run.SubjectRole is RoleKind.PlanningLead or RoleKind.ImplementationLead && run.WorkItemId is null
        };
        if (!allowed) throw new GovernanceException("Routing judgment does not match the producer role and work scope.");
        foreach (var evidenceId in assessment.EvidenceIds)
            if (!state.Evidence.TryGetValue(evidenceId, out var evidence) || evidence.Provenance.ActorId != actor ||
                evidence.Provenance.RecordedAt < run.StartedAt)
                throw new GovernanceException("Routing evidence must be recorded by this producer during this run.");
    }
}
