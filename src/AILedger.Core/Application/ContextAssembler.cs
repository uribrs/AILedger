using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.Application;

public sealed class ContextAssembler : IContextAssembler
{
    private static readonly IReadOnlySet<ContextArtifactKind> AlwaysIncludedKinds =
        new HashSet<ContextArtifactKind>
        {
            ContextArtifactKind.Rules,
            ContextArtifactKind.Skill,
            ContextArtifactKind.TaskGoal,
            ContextArtifactKind.Constraint,
            ContextArtifactKind.StopCondition
        };

    private static readonly IReadOnlySet<ContextArtifactKind> ReviewerExclusions =
        new HashSet<ContextArtifactKind>
        {
            ContextArtifactKind.UserRequest,
            ContextArtifactKind.PromptContract,
            ContextArtifactKind.OrchestrationPlan,
            ContextArtifactKind.VerifierOutput
        };

    private static readonly IReadOnlyDictionary<RoleKind, IReadOnlySet<string>> CanonicalRoleSkills =
        new Dictionary<RoleKind, IReadOnlySet<string>>
        {
            [RoleKind.PlanningLead] = Skills(
                "workflow-coordinator",
                "prompt-contract-designer",
                "task-orchestrator",
                "technical-researcher"),
            [RoleKind.ImplementationLead] = Skills(
                "workflow-coordinator",
                "prompt-contract-designer",
                "task-orchestrator",
                "contract-driven-execution"),
            [RoleKind.Researcher] = Skills("technical-researcher"),
            [RoleKind.Worker] = Skills("contract-driven-execution"),
            [RoleKind.Verifier] = Skills("task-orchestrator"),
            [RoleKind.CodeReviewer] = Skills("code-reviewer")
        };

    public ContextManifest Build(
        GovernedTaskState state,
        ActorId actorId,
        WorkItemId? workItemId,
        IReadOnlyList<ContextArtifact> availableArtifacts,
        DateTimeOffset assembledAt)
    {
        var assignment = GetAssignment(state, actorId);
        EnsureCanBuildContext(assignment);
        var workItem = GetWorkItem(state, workItemId);
        var stateArtifacts = BuildStateArtifacts(state, workItem);
        var relevantIds = stateArtifacts
            .SelectMany(artifact => artifact.RelatedIds.Append(artifact.Id))
            .ToHashSet(StringComparer.Ordinal);

        var artifacts = stateArtifacts
            .Concat(availableArtifacts)
            .Where(artifact => IsAllowedForRole(assignment.Role, artifact))
            .Where(artifact => IsRelevant(artifact, workItem, relevantIds))
            .OrderBy(artifact => artifact.Kind)
            .ThenBy(artifact => artifact.Id, StringComparer.Ordinal)
            .ThenBy(artifact => artifact.Content, StringComparer.Ordinal)
            .GroupBy(artifact => (artifact.Kind, artifact.Id))
            .Select(group => group.First())
            .ToArray();

        var stopConditions = artifacts
            .Where(artifact => artifact.Kind == ContextArtifactKind.StopCondition)
            .Select(artifact => artifact.Content)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return new ContextManifest(
            GovernedTaskState.CurrentSchemaVersion,
            state.TaskId,
            actorId,
            assignment.Role,
            workItemId,
            state.Version,
            assignment.Capabilities.OrderBy(value => value).ToArray(),
            artifacts,
            stopConditions,
            assembledAt);
    }

    private static RoleAssignment GetAssignment(GovernedTaskState state, ActorId actorId)
    {
        if (!state.Roles.TryGetValue(actorId, out var assignment))
        {
            throw new GovernanceException($"Actor '{actorId}' has no assigned role.");
        }

        return assignment;
    }

    private static void EnsureCanBuildContext(RoleAssignment assignment)
    {
        if (!assignment.Capabilities.Contains(Capability.BuildContext))
        {
            throw new GovernanceException($"Actor '{assignment.ActorId}' lacks capability '{Capability.BuildContext}'.");
        }
    }

    private static WorkItem? GetWorkItem(GovernedTaskState state, WorkItemId? workItemId)
    {
        if (workItemId is null)
        {
            return null;
        }

        if (!state.WorkItems.TryGetValue(workItemId.Value, out var workItem))
        {
            throw new GovernanceException($"Unknown work item '{workItemId.Value}'.");
        }

        return workItem;
    }

    private static bool IsAllowedForRole(RoleKind role, ContextArtifact artifact)
    {
        if (role == RoleKind.CodeReviewer && ReviewerExclusions.Contains(artifact.Kind))
        {
            return false;
        }

        if (artifact.Kind != ContextArtifactKind.Skill || role == RoleKind.Operator)
        {
            return true;
        }

        if (artifact.RelatedIds.Contains(role.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return CanonicalRoleSkills.TryGetValue(role, out var skills) &&
               skills.Any(skill => IsSkillId(artifact.Id, skill));
    }

    private static bool IsRelevant(
        ContextArtifact artifact,
        WorkItem? workItem,
        IReadOnlySet<string> relevantIds)
    {
        if (AlwaysIncludedKinds.Contains(artifact.Kind) || workItem is null)
        {
            return true;
        }

        return relevantIds.Contains(artifact.Id) || artifact.RelatedIds.Any(relevantIds.Contains);
    }

    private static IReadOnlyList<ContextArtifact> BuildStateArtifacts(
        GovernedTaskState state,
        WorkItem? workItem)
    {
        var artifacts = new List<ContextArtifact>
        {
            new(ContextArtifactKind.TaskGoal, "task-goal", state.Goal, [state.TaskId.Value]),
            new(ContextArtifactKind.TaskGoal, "task-stage", state.Stage.ToString(), [state.TaskId.Value])
        };

        var claims = SelectClaims(state, workItem);
        var claimIds = claims.Select(claim => claim.Id).ToHashSet();
        var decisions = state.Decisions.Values
            .Where(decision => workItem is null || decision.DependsOnClaims.Any(claimIds.Contains))
            .OrderBy(decision => decision.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var evidence = state.Evidence.Values
            .Where(item => workItem is null || item.Supports.Any(claimIds.Contains) || item.Refutes.Any(claimIds.Contains))
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToArray();

        artifacts.AddRange(claims.Select(ToArtifact));
        artifacts.AddRange(decisions.Select(ToArtifact));
        artifacts.AddRange(evidence.Select(ToArtifact));
        if (workItem is not null)
        {
            artifacts.Add(ToArtifact(workItem));
        }
        else
        {
            artifacts.AddRange(state.WorkItems.Values
                .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                .Select(ToArtifact));
        }

        return artifacts;
    }

    private static IReadOnlyList<Claim> SelectClaims(GovernedTaskState state, WorkItem? workItem) =>
        state.Claims.Values
            .Where(claim => workItem is null || workItem.DependsOnClaims.Contains(claim.Id))
            .OrderBy(claim => claim.Id.Value, StringComparer.Ordinal)
            .ToArray();

    private static ContextArtifact ToArtifact(Claim claim) =>
        new(
            ContextArtifactKind.Claim,
            claim.Id.Value,
            $"{claim.Status}: {claim.Statement}",
            claim.EvidenceIds.Select(id => id.Value).OrderBy(id => id, StringComparer.Ordinal).ToArray());

    private static ContextArtifact ToArtifact(Decision decision) =>
        new(
            ContextArtifactKind.Decision,
            decision.Id.Value,
            $"{decision.Status}: {decision.Statement}{Environment.NewLine}Rationale: {decision.Rationale}",
            decision.DependsOnClaims.Select(id => id.Value).OrderBy(id => id, StringComparer.Ordinal).ToArray());

    private static ContextArtifact ToArtifact(Evidence evidence) =>
        new(
            ContextArtifactKind.Evidence,
            evidence.Id.Value,
            $"{evidence.SourceType}: {evidence.Citation}{Environment.NewLine}{evidence.Summary}",
            evidence.Supports.Concat(evidence.Refutes)
                .Select(id => id.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray());

    private static ContextArtifact ToArtifact(WorkItem workItem) =>
        new(
            ContextArtifactKind.WorkItem,
            workItem.Id.Value,
            $"{workItem.Status}: {workItem.Title}{Environment.NewLine}Scope: {string.Join(", ", workItem.ResourceScope)}",
            workItem.DependsOnClaims.Select(id => id.Value).OrderBy(id => id, StringComparer.Ordinal).ToArray());

    private static IReadOnlySet<string> Skills(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    private static bool IsSkillId(string artifactId, string skillName) =>
        artifactId.Equals(skillName, StringComparison.OrdinalIgnoreCase) ||
        artifactId.EndsWith($"/{skillName}", StringComparison.OrdinalIgnoreCase) ||
        artifactId.EndsWith($":{skillName}", StringComparison.OrdinalIgnoreCase);
}
