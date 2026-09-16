using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;

namespace AILedger.Core.WorkItems;

// Command-time evidence that a work item was performed and independently verified. Replay does not
// re-derive these gates because historical runs predate the subject-role and sequencing requirements.
internal static class WorkItemVerificationRules
{
    // The positive list is deliberate: a role introduced later must not silently begin counting as work.
    // Researcher work satisfies a work item even though only Worker satisfies the Execution-stage arm;
    // those predicates answer different questions and must not be aligned accidentally.
    internal static bool DidWorkUnderAWorkingRole(AgentRun run) =>
        DidWork(run) && run.SubjectRole is RoleKind.Worker or RoleKind.Researcher;

    internal static bool HasCompletedWorkingRun(GovernedTaskState state, WorkItemId workItemId) =>
        state.Runs.Values.Any(run => run.WorkItemId == workItemId && DidWorkUnderAWorkingRole(run));

    /// <summary>
    /// A completed run that actually launched cognition. A declared no-provider run only records
    /// coordination and cannot satisfy work or verification gates.
    /// </summary>
    internal static bool DidWork(AgentRun run) =>
        run.Status is AgentRunStatus.Completed && !run.HasNoProviderSessionByDeclaration;

    internal static bool HasCompletedVerifierRun(GovernedTaskState state, WorkItemId workItemId) =>
        state.Runs.Values.Any(run =>
            run.WorkItemId == workItemId &&
            DidWork(run) &&
            run.SubjectRole is RoleKind.Verifier);

    private static AgentRun? LatestCompletedWorkingRun(GovernedTaskState state, WorkItemId workItemId) =>
        state.Runs.Values
            .Where(run => run.WorkItemId == workItemId && DidWorkUnderAWorkingRole(run))
            .OrderByDescending(run => run.EndedAt)
            .FirstOrDefault();

    internal static bool HasVerifierRunAfterLatestWork(GovernedTaskState state, WorkItemId workItemId)
    {
        var workedAt = LatestCompletedWorkingRun(state, workItemId)?.EndedAt;
        return state.Runs.Values.Any(run =>
            run.WorkItemId == workItemId &&
            DidWork(run) &&
            run.SubjectRole is RoleKind.Verifier &&
            (workedAt is null || run.EndedAt >= workedAt));
    }

    internal static bool HasCurrentCodeReviewAfterLatestVerification(
        GovernedTaskState state,
        WorkItemId workItemId)
    {
        var workedAt = LatestCompletedWorkingRun(state, workItemId)?.EndedAt;
        var currentReviewRunIds = ArtifactRevisionRules.Current(state)
            .Where(artifact =>
                artifact.Kind == GovernedArtifactKind.CodeReviewOutput &&
                artifact.WorkItemId == workItemId &&
                artifact.ProducerRunId is not null)
            .Select(artifact => artifact.ProducerRunId!.Value)
            .ToHashSet();

        return state.Runs.Values.Any(review =>
            review.WorkItemId == workItemId &&
            DidWork(review) &&
            review.SubjectRole == RoleKind.CodeReviewer &&
            currentReviewRunIds.Contains(review.Id) &&
            (workedAt is null || review.EndedAt >= workedAt) &&
            state.Runs.Values.Any(verifier =>
                verifier.WorkItemId == workItemId &&
                DidWork(verifier) &&
                verifier.SubjectRole == RoleKind.Verifier &&
                (workedAt is null || verifier.EndedAt >= workedAt) &&
                review.EndedAt >= verifier.EndedAt));
    }

    // Returns the working provider when every qualifying verifier used that same provider. Null means
    // either there was no work to compare or at least one independent provider verified the result.
    internal static string? ProviderThatVerifiedItsOwnWork(GovernedTaskState state, WorkItemId workItemId)
    {
        if (LatestCompletedWorkingRun(state, workItemId) is not { } worked)
        {
            return null;
        }

        var verifiedByAnotherProvider = state.Runs.Values.Any(run =>
            run.WorkItemId == workItemId &&
            DidWork(run) &&
            run.SubjectRole is RoleKind.Verifier &&
            (worked.EndedAt is null || run.EndedAt >= worked.EndedAt) &&
            !string.Equals(run.Provider, worked.Provider, StringComparison.OrdinalIgnoreCase));

        return verifiedByAnotherProvider ? null : worked.Provider;
    }
}
