using AILedger.Core.ContextBriefing;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Runs;

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
        DateTimeOffset assembledAt,
        IReadOnlyList<WorkItemId>? coveredWorkItemIds = null,
        AssuranceBinding? assurance = null)
    {
        var assignment = GetAssignment(state, actorId);
        EnsureCanBuildContext(assignment);
        // R4 (reviewer-narrative-isolation): a refresh cannot downgrade a bound review.
        if (assurance is null && assignment.Role == RoleKind.CodeReviewer &&
            state.Runs.Values.Any(run => run.ActorId == actorId &&
                run.Status == AgentRunStatus.Active && run.Assurance?.VerifierRunId is not null))
        {
            throw new GovernanceException(
                "Assurance coverage: an active bound reviewer cannot build unbound context; use the supplied frozen manifest.");
        }

        return Assemble(state, assignment, workItemId, availableArtifacts, assembledAt,
            coveredWorkItemIds, assurance);
    }

    public ContextManifest BuildForRun(
        GovernedTaskState state,
        ActorId actorId,
        RunId runId,
        IReadOnlyList<ContextArtifact> availableArtifacts,
        DateTimeOffset assembledAt)
    {
        var assignment = GetAssignment(state, actorId);
        EnsureCanBuildContext(assignment);
        if (!state.Runs.TryGetValue(runId, out var run))
            throw new GovernanceException($"Context run '{runId}': run does not exist.");
        if (run.ActorId != actorId)
            throw new GovernanceException($"Context run '{runId}': run belongs to actor '{run.ActorId}', not '{actorId}'.");
        if (run.Status != AgentRunStatus.Active)
            throw new GovernanceException($"Context run '{runId}': run must be Active; current status is '{run.Status}'.");

        // The admitted run identifies this launch even when a disjoint bound review is active.
        return Assemble(state, assignment, run.WorkItemId, availableArtifacts, assembledAt,
            run.Assurance?.WorkItemIds, run.Assurance);
    }

    private static ContextManifest Assemble(
        GovernedTaskState state,
        RoleAssignment assignment,
        WorkItemId? workItemId,
        IReadOnlyList<ContextArtifact> availableArtifacts,
        DateTimeOffset assembledAt,
        IReadOnlyList<WorkItemId>? coveredWorkItemIds,
        AssuranceBinding? assurance)
    {
        var workItem = GetWorkItem(state, workItemId);
        var members = WorkCoverage.Normalize(workItemId, coveredWorkItemIds ?? assurance?.WorkItemIds);
        if (assurance is not null && !members.SequenceEqual(AssuranceRules.ValidateShape(workItemId, assurance)))
            throw new GovernanceException("Assurance coverage: context selection must equal assurance membership.");
        var selected = members.Select(member => GetWorkItem(state, member)!).ToArray();
        var isolatedReviewer = assurance is not null && assignment.Role == RoleKind.CodeReviewer;
        var stateArtifacts = isolatedReviewer ? Array.Empty<ContextArtifact>() :
            selected.Length == 0 ? ContextArtifactProjection.Build(state, null).ToArray() :
            selected.SelectMany(item => ContextArtifactProjection.Build(state, item)).ToArray();
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
            assignment.ActorId,
            assignment.Role,
            workItemId,
            state.Version,
            assignment.Capabilities.OrderBy(value => value).ToArray(),
            artifacts,
            stopConditions,
            assembledAt,
            coveredWorkItemIds is null && assurance is null ? null : members.ToArray(),
            assurance is null ? null : AssuranceRules.Copy(assurance),
            isolatedReviewer ? selected.Select(item => new ReviewWorkItem(item.Id, item.ResourceScope.ToArray(), item.BaseRef)).ToArray() : null);
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
