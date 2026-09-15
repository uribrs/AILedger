using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.ContextBriefing;

// Renders replayed task state into the context artifacts an assembler can filter and order.
internal static class ContextArtifactProjection
{
    internal static IReadOnlyList<ContextArtifact> Build(
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
            .Where(IsBriefable)
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

    // Every revision is retained in state, but only the artifact nothing supersedes is current.
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
    // this agent's context.
    private static bool IsInWorkScope(GovernedArtifact artifact, WorkItem? workItem) =>
        workItem is null || artifact.WorkItemId is null || artifact.WorkItemId == workItem.Id;

    // A workflow retrospective scores the agents that worked the task, so it is withheld from
    // every role. The default conversion arm remains exhaustive for future artifact kinds.
    private static bool IsBriefable(GovernedArtifact artifact) =>
        artifact.Kind != GovernedArtifactKind.WorkflowRetrospective;

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
            $"{workItem.Status}: {workItem.Title}{Environment.NewLine}Scope: {string.Join(", ", workItem.ResourceScope)}" +
            (workItem.BaseRef is null ? string.Empty : $"{Environment.NewLine}Base ref: {workItem.BaseRef}"),
            workItem.DependsOnClaims.Select(id => id.Value).OrderBy(id => id, StringComparer.Ordinal).ToArray());

    private static ContextArtifact ToArtifact(Lesson lesson) =>
        new(
            ContextArtifactKind.Lesson,
            lesson.Id.Value,
            $"Unverified prior evidence from {lesson.SourceTaskId}/{lesson.SourceKind}/{lesson.SourceRecordId}" +
            (lesson.Class is null ? string.Empty : $" [{lesson.Class}]") +
            (lesson.Repo is null ? string.Empty : $" in {lesson.Repo}") +
            "; re-establish before relying on it: " +
            $"{lesson.Statement}{Environment.NewLine}Outcome: {lesson.Outcome}" +
            (lesson.Tags is null || lesson.Tags.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}Tags: {string.Join(", ", lesson.Tags)}") +
            $"{Environment.NewLine}Kind: {lesson.EffectiveKind()}" +
            (lesson.Audience is null || lesson.Audience.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}Audience: {string.Join(", ", lesson.Audience)}") +
            (lesson.Citations.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}Evidence: {string.Join(" | ", lesson.Citations)}") +
            (lesson.Verify is null
                ? string.Empty
                : $"{Environment.NewLine}Verify: {lesson.Verify}" +
                  (lesson.VerifyExpects is null
                      ? string.Empty
                      : $" (expects {lesson.VerifyExpects})")) +
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
}
