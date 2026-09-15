using AILedger.Core.ContextBriefing;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.Application;

// Public namespace is preserved for source compatibility; physical ownership belongs to Context Briefing.
public sealed class ContextAssembler : IContextAssembler
{
    private static readonly IReadOnlySet<ContextArtifactKind> AlwaysIncludedKinds =
        new HashSet<ContextArtifactKind>
        {
            ContextArtifactKind.Rules,
            ContextArtifactKind.Skill,
            ContextArtifactKind.TaskGoal,
            ContextArtifactKind.Constraint,
            ContextArtifactKind.StopCondition,
            // Discarded approaches, open escalations, lessons, and marks govern the whole task.
            ContextArtifactKind.Escalation,
            ContextArtifactKind.Alternative,
            ContextArtifactKind.Lesson,
            ContextArtifactKind.LessonMark,
            ContextArtifactKind.UserRequest,
            ContextArtifactKind.PromptContract,
            ContextArtifactKind.OrchestrationPlan
        };

    // The five governed workflow kinds come from replayed state. The cognitive root is the only
    // input without an event behind it and legitimately arrives from outside.
    private static readonly IReadOnlySet<ContextArtifactKind> CallerSuppliedKinds =
        new HashSet<ContextArtifactKind>
        {
            ContextArtifactKind.Rules,
            ContextArtifactKind.Skill,
            ContextArtifactKind.StopCondition
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
        var stateArtifacts = ContextArtifactProjection.Build(state, workItem);
        var relevantIds = stateArtifacts
            .SelectMany(artifact => artifact.RelatedIds.Append(artifact.Id))
            .ToHashSet(StringComparer.Ordinal);
        var lessonAudiences = state.Lessons.Values
            .Where(lesson => lesson.Audience is { Count: > 0 })
            .ToDictionary(lesson => lesson.Id.Value, lesson => lesson.Audience!, StringComparer.Ordinal);

        var artifacts = stateArtifacts
            .Concat(availableArtifacts.Where(artifact => CallerSuppliedKinds.Contains(artifact.Kind)))
            .Where(artifact => ContextRolePolicy.IsAllowed(assignment.Role, artifact, lessonAudiences))
            .Where(artifact => IsRelevant(artifact, workItem, relevantIds))
            .OrderBy(artifact => artifact.Kind)
            .ThenBy(artifact => ContextRolePolicy.SkillRank(assignment.Role, artifact))
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

    public IReadOnlyList<ContextSkill> SkillsServed(
        RoleKind role,
        IReadOnlyList<ContextArtifact> availableArtifacts) =>
        ContextRolePolicy.SkillsServed(role, availableArtifacts);

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
            throw new GovernanceException(
                $"Actor '{assignment.ActorId}' lacks capability '{Capability.BuildContext}'.");
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
}
