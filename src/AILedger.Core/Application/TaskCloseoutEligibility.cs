using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;

namespace AILedger.Core.Application;

// Cleanup eligibility belongs after closeout, not on the Archive transition. Each condition is a
// separate fact so callers can report what is missing without interpreting one aggregate refusal.
public static class TaskCloseoutEligibility
{
    public static TaskCloseoutEligibilityReport Evaluate(GovernedTaskState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var liveWork = state.WorkItems.Values
            .Where(item => item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Stale
                or WorkItemStatus.Abandoned))
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var activeRuns = state.Runs.Values
            .Where(run => run.Status == AgentRunStatus.Active)
            .OrderBy(run => run.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var openEscalations = state.Escalations.Values
            .Where(escalation => escalation.Status == EscalationStatus.Open)
            .OrderBy(escalation => escalation.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var openChallenges = state.Challenges.Values
            .Where(challenge => challenge.Status == ChallengeStatus.Open)
            .OrderBy(challenge => challenge.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var current = ArtifactApplicability.Current(state);
        var synthesis = Current(current, GovernedArtifactKind.CloseoutSynthesis);
        var retrospective = Current(current, GovernedArtifactKind.WorkflowRetrospective);
        var mintedLesson = state.Lessons.Values.Any(lesson => lesson.SourceTaskId == state.TaskId);
        // Publication is owed only when the synthesis names a lesson other than the explicit
        // "none" value. A missing synthesis cannot use that exemption because no closeout record
        // has established whether there was anything to publish.
        var lessonPublicationComplete = mintedLesson ||
            synthesis is not null && !HasLessonToMint(synthesis);
        var lessonPublicationDetail = synthesis is null
            ? "No current closeout synthesis is filed, so lesson publication cannot be evaluated."
            : "The current closeout synthesis records a lesson to mint, but this task minted no " +
                "lesson of its own.";

        return new TaskCloseoutEligibilityReport(
            state.TaskId,
            state.Stage,
            state.Version,
            [
                Check("stageIsArchive", state.Stage == TaskStage.Archive,
                    $"The task is at stage '{state.Stage}'; cleanup follows Archive."),
                Check("noLiveWorkItems", liveWork.Length == 0,
                    liveWork.Length == 0 ? null
                        : $"Work item '{liveWork[0].Id}' is live in status '{liveWork[0].Status}'."),
                Check("noActiveRuns", activeRuns.Length == 0,
                    activeRuns.Length == 0 ? null : $"Run '{activeRuns[0].Id}' is still active."),
                Check("noOpenEscalations", openEscalations.Length == 0,
                    openEscalations.Length == 0 ? null
                        : $"Escalation '{openEscalations[0].Id}' is still open."),
                Check("noOpenChallenges", openChallenges.Length == 0,
                    openChallenges.Length == 0 ? null
                        : $"Challenge '{openChallenges[0].Id}' is still open."),
                Check("closeoutSynthesisFiled", synthesis is not null,
                    "No current closeout synthesis is filed. Nothing has established what this " +
                    "task's assurance found, so nothing has established what is disposable."),
                Check("workflowRetrospectiveFiled", retrospective is not null,
                    "No current workflow retrospective is filed."),
                Check("lessonsMinted", lessonPublicationComplete,
                    lessonPublicationDetail)
            ],
            synthesis?.ArtifactId.Value,
            retrospective?.ArtifactId.Value);
    }

    private static GovernedArtifact? Current(
        IReadOnlyList<GovernedArtifact> current,
        GovernedArtifactKind kind) =>
        current.Where(artifact => artifact.Kind == kind)
            .OrderBy(artifact => artifact.ArtifactId.Value, StringComparer.Ordinal)
            .FirstOrDefault();

    private static bool HasLessonToMint(GovernedArtifact synthesis) =>
        MarkdownTableReader.Read(
            synthesis.Content,
            CloseoutSynthesisRules.FindingsHeader,
            CloseoutSynthesisRules.FindingsRefusal())
        .Any(row => !string.Equals(row[8], "none", StringComparison.OrdinalIgnoreCase));

    private static CloseoutEligibilityCheck Check(string name, bool satisfied, string? detail) =>
        new(name, satisfied, satisfied ? null : detail);
}

public sealed record CloseoutEligibilityCheck(string Name, bool Satisfied, string? Detail);

public sealed record TaskCloseoutEligibilityReport(
    TaskId Task,
    TaskStage Stage,
    long Version,
    IReadOnlyList<CloseoutEligibilityCheck> Checks,
    string? CloseoutSynthesisArtifactId,
    string? WorkflowRetrospectiveArtifactId)
{
    public bool Eligible => Checks.All(check => check.Satisfied);

    public IReadOnlyList<CloseoutEligibilityCheck> Unsatisfied =>
        Checks.Where(check => !check.Satisfied).ToArray();
}
