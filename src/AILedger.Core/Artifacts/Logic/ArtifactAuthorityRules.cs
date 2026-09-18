using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Artifacts;

internal static class ArtifactAuthorityRules
{
    internal static void Ensure(
        GovernedTaskState state,
        GovernedArtifactKind kind,
        ActorId actorId,
        RoleKind actorRole,
        WorkItemId? workItemId,
        RunId? producerRunId)
    {
        if (kind == GovernedArtifactKind.UserRequest)
        {
            if (actorRole != RoleKind.Operator || producerRunId is not null)
            {
                throw new GovernanceException(
                    "A user-request artifact must be operator-authored and cannot name a producer run.");
            }

            return;
        }

        // These task-wide closeout artifacts may be filed when no run is active. Their authority
        // therefore comes from the actor's assignment rather than a producer run.
        if (kind is GovernedArtifactKind.WorkflowRetrospective or GovernedArtifactKind.CloseoutSynthesis)
        {
            EnsureNoProducerAuthority(kind, actorRole, producerRunId);
            return;
        }

        if (producerRunId is not { } runId)
        {
            throw new GovernanceException($"A '{kind}' artifact must name its active producer run.");
        }

        var run = Get(state.Runs, runId, "producer run");
        if (run.Status != AgentRunStatus.Active || run.ActorId != actorId)
        {
            throw new GovernanceException(
                $"Artifact producer run '{runId}' must be active and belong to actor '{actorId}'.");
        }

        if (workItemId is not null && run.WorkItemId != workItemId)
        {
            throw new GovernanceException($"Artifact work item must match producer run '{runId}'.");
        }

        var requiredRole = kind switch
        {
            GovernedArtifactKind.VerifierOutput => RoleKind.Verifier,
            GovernedArtifactKind.CodeReviewOutput => RoleKind.CodeReviewer,
            _ => (RoleKind?)null
        };
        if (requiredRole is { } role && run.SubjectRole != role)
        {
            throw new GovernanceException($"A '{kind}' artifact requires a matching active {role} run.");
        }

        if (requiredRole is null && run.SubjectRole is not
            (RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead))
        {
            throw new GovernanceException(
                "Only an operator, planning lead, or implementation lead can record a governing workflow artifact.");
        }
    }

    private static void EnsureNoProducerAuthority(
        GovernedArtifactKind kind,
        RoleKind actorRole,
        RunId? producerRunId)
    {
        if (producerRunId is not null)
        {
            throw new GovernanceException(kind == GovernedArtifactKind.WorkflowRetrospective
                ? "A workflow-retrospective artifact is recorded after closeout and cannot name a producer run."
                : "A closeout-synthesis artifact is task-wide judgement over the whole record and " +
                  "cannot name a producer run; it is filed by an operator or a lead, from Review " +
                  "onward, including after the task is archived.");
        }

        if (actorRole is not (RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead))
        {
            throw new GovernanceException(
                "Only an operator, planning lead, or implementation lead can record a governing workflow artifact.");
        }
    }
}
