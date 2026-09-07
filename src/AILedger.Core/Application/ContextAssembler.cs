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
            ContextArtifactKind.StopCondition,
            // A discarded approach and an open escalation are only useful if they are
            // seen regardless of which work item is in hand.
            ContextArtifactKind.Escalation,
            ContextArtifactKind.Alternative,
            ContextArtifactKind.Lesson,
            ContextArtifactKind.LessonMark,
            // The request, the contract and the plan govern the whole task, not one item in it.
            // Work-item relevance admits an artifact only when it names a dependent claim, so
            // before they were always-included a work-scoped manifest carried no contract at all.
            ContextArtifactKind.UserRequest,
            ContextArtifactKind.PromptContract,
            ContextArtifactKind.OrchestrationPlan
        };

    private static readonly IReadOnlySet<ContextArtifactKind> ReviewerExclusions =
        new HashSet<ContextArtifactKind>
        {
            ContextArtifactKind.UserRequest,
            ContextArtifactKind.PromptContract,
            ContextArtifactKind.OrchestrationPlan,
            ContextArtifactKind.VerifierOutput,
            // An earlier review of the same work is withheld for the same reason as the verifier's
            // verdict: a second pass that reads the first is not a second opinion.
            ContextArtifactKind.CodeReviewOutput,
            // An open escalation carries the leads' recommendation, which is intent. A blind
            // review must not learn what the team wants the answer to be. Alternatives stay:
            // a discarded approach is design history, like a Decision, not a verdict.
            ContextArtifactKind.Escalation
        };

    // What the caller may still contribute. The five governed workflow kinds now come from replayed
    // state, so a caller offering one is offering a task record the ledger never wrote; the cognitive
    // root is the only input that has no event behind it and legitimately arrives from outside.
    private static readonly IReadOnlySet<ContextArtifactKind> CallerSuppliedKinds =
        new HashSet<ContextArtifactKind>
        {
            ContextArtifactKind.Rules,
            ContextArtifactKind.Skill,
            ContextArtifactKind.StopCondition
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
            .Concat(availableArtifacts.Where(artifact => CallerSuppliedKinds.Contains(artifact.Kind)))
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
        artifacts.AddRange(state.Constraints.Values
            .Where(constraint => constraint.Status == ConstraintStatus.Active)
            .OrderBy(constraint => constraint.Id.Value, StringComparer.Ordinal)
            .Select(ToArtifact));
        artifacts.AddRange(state.Alternatives.Values
            .OrderBy(alternative => alternative.Id.Value, StringComparer.Ordinal)
            .Select(ToArtifact));
        artifacts.AddRange(state.Escalations.Values
            .Where(escalation => escalation.Status == EscalationStatus.Open)
            .OrderBy(escalation => escalation.Id.Value, StringComparer.Ordinal)
            .Select(ToArtifact));
        artifacts.AddRange(state.Lessons.Values
            .OrderBy(lesson => lesson.Id.Value, StringComparer.Ordinal)
            .Select(ToArtifact));
        artifacts.AddRange(state.LessonMarks.Values
            .OrderBy(mark => mark.Id.Value, StringComparer.Ordinal)
            .Select(ToArtifact));
        artifacts.AddRange(CurrentArtifacts(state)
            .Where(artifact => IsInWorkScope(artifact, workItem))
            .OrderBy(artifact => artifact.ArtifactId.Value, StringComparer.Ordinal)
            .Select(ToArtifact));
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

    // Every revision is retained in state, but only the artifact nothing supersedes is current. A
    // superseded contract in a manifest is an agent working to instructions that were withdrawn.
    private static IReadOnlyList<GovernedArtifact> CurrentArtifacts(GovernedTaskState state)
    {
        var superseded = state.Artifacts.Values
            .Where(artifact => artifact.SupersedesArtifactId is not null)
            .Select(artifact => artifact.SupersedesArtifactId!.Value)
            .ToHashSet();
        return state.Artifacts.Values
            .Where(artifact => !superseded.Contains(artifact.ArtifactId))
            .ToArray();
    }

    // A verifier or review output belongs to one work item, and the other items' outputs are not
    // this agent's context. The filter is here rather than in IsRelevant because the relevant-id set
    // is drawn from the state artifacts themselves: an output naming another work item would put
    // that item's id into the set and so admit itself.
    private static bool IsInWorkScope(GovernedArtifact artifact, WorkItem? workItem) =>
        workItem is null || artifact.WorkItemId is null || artifact.WorkItemId == workItem.Id;

    private static ContextArtifact ToArtifact(GovernedArtifact artifact) =>
        new(
            ToContextKind(artifact.Kind),
            artifact.ArtifactId.Value,
            $"{artifact.Title}{Environment.NewLine}{artifact.Content}",
            artifact.WorkItemId is { } workItemId ? [workItemId.Value] : []);

    private static ContextArtifactKind ToContextKind(GovernedArtifactKind kind) => kind switch
    {
        GovernedArtifactKind.UserRequest => ContextArtifactKind.UserRequest,
        GovernedArtifactKind.PromptContract => ContextArtifactKind.PromptContract,
        GovernedArtifactKind.OrchestrationPlan => ContextArtifactKind.OrchestrationPlan,
        GovernedArtifactKind.VerifierOutput => ContextArtifactKind.VerifierOutput,
        GovernedArtifactKind.CodeReviewOutput => ContextArtifactKind.CodeReviewOutput,
        _ => throw new GovernanceException($"Unknown governed artifact kind '{kind}'.")
    };

    private static ContextArtifact ToArtifact(Constraint constraint) =>
        new(
            ContextArtifactKind.Constraint,
            constraint.Id.Value,
            $"{constraint.Statement}{Environment.NewLine}Source: {constraint.Source}",
            constraint.Scope.OrderBy(entry => entry, StringComparer.Ordinal).ToArray());

    private static ContextArtifact ToArtifact(Alternative alternative) =>
        new(
            ContextArtifactKind.Alternative,
            alternative.Id.Value,
            $"Rejected: {alternative.Statement}{Environment.NewLine}Because: {alternative.RejectionRationale}",
            alternative.ReplacedByDecisionId is { } decisionId ? [decisionId.Value] : []);

    private static ContextArtifact ToArtifact(Escalation escalation) =>
        new(
            ContextArtifactKind.Escalation,
            escalation.Id.Value,
            $"{escalation.Kind} awaiting the operator: {escalation.Question}" +
            (escalation.Options.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}Options: {string.Join(" | ", escalation.Options)}") +
            (escalation.Recommendation is null
                ? string.Empty
                : $"{Environment.NewLine}Recommended: {escalation.Recommendation}"),
            escalation.AttemptEvidenceIds.Select(id => id.Value)
                .Concat(escalation.WorkItemId is { } workItemId ? [workItemId.Value] : Array.Empty<string>())
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray());

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

    private static ContextArtifact ToArtifact(Lesson lesson) =>
        new(
            ContextArtifactKind.Lesson,
            lesson.Id.Value,
            $"Unverified prior evidence from {lesson.SourceTaskId}/{lesson.SourceKind}/{lesson.SourceRecordId}" +
            (lesson.Class is null ? string.Empty : $" [{lesson.Class}]") +
            (lesson.Repo is null ? string.Empty : $" in {lesson.Repo}") +
            $"; re-establish before relying on it: " +
            $"{lesson.Statement}{Environment.NewLine}Outcome: {lesson.Outcome}" +
            (lesson.Tags is null || lesson.Tags.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}Tags: {string.Join(", ", lesson.Tags)}") +
            (lesson.Citations.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}Evidence: {string.Join(" | ", lesson.Citations)}") +
            // The verify command is what turns re-establishing the lesson from an instruction into
            // something the reader can run, so it belongs beside the label that demands it.
            (lesson.Verify is null
                ? string.Empty
                : $"{Environment.NewLine}Verify: {lesson.Verify}") +
            (lesson.DoNot is null
                ? string.Empty
                : $"{Environment.NewLine}Do not: {lesson.DoNot}") +
            (lesson.Actor is null
                ? string.Empty
                : $"{Environment.NewLine}Established by: {lesson.Actor}"),
            [lesson.SourceTaskId.Value, lesson.SourceRecordId]);

    private static ContextArtifact ToArtifact(LessonMark mark) =>
        new(
            ContextArtifactKind.LessonMark,
            mark.Id.Value,
            $"Lesson-bearing: {mark.SourceKind}/{mark.SourceRecordId}" +
            (mark.SupersedesLessonId is null
                ? string.Empty
                : $"{Environment.NewLine}Supersedes: {mark.SupersedesLessonId}"),
            [mark.SourceRecordId]);

    private static IReadOnlySet<string> Skills(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    private static bool IsSkillId(string artifactId, string skillName) =>
        artifactId.Equals(skillName, StringComparison.OrdinalIgnoreCase) ||
        artifactId.EndsWith($"/{skillName}", StringComparison.OrdinalIgnoreCase) ||
        artifactId.EndsWith($":{skillName}", StringComparison.OrdinalIgnoreCase);
}
