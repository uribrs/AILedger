namespace AILedger.Core.Contracts;

/// <summary>Projects applicable members from immutable coverage and persisted replacement edges.</summary>
public static class ArtifactApplicability
{
    public static IReadOnlyList<WorkItemId> CurrentMembers(
        GovernedTaskState state, GovernedArtifact artifact)
    {
        if (IsGloballySuperseded(state, artifact))
        {
            return [];
        }

        var replacedMembers = state.Artifacts.Values
            .Where(item => item.Assurance is not null)
            .SelectMany(item => item.MemberReplacements ?? [])
            .Where(row => row.ArtifactIds.Contains(artifact.ArtifactId))
            .Select(row => row.WorkItemId)
            .ToHashSet();

        // Admission/replay validates edges against pre-event heads. Timestamps and dictionary
        // iteration order cannot establish replacement authority and are deliberately not used.
        return WorkCoverage.Effective(artifact.WorkItemId, artifact.Assurance)
            .Where(member => !replacedMembers.Contains(member))
            .ToArray();
    }

    public static IReadOnlyList<GovernedArtifact> Current(GovernedTaskState state) =>
        state.Artifacts.Values.Where(artifact =>
            artifact.WorkItemId is null && artifact.Assurance is null
                ? !IsGloballySuperseded(state, artifact)
                : CurrentMembers(state, artifact).Count > 0)
            .ToArray();

    private static bool IsGloballySuperseded(GovernedTaskState state, GovernedArtifact artifact) =>
        state.Artifacts.Values.Any(item =>
            item.Assurance is null && item.SupersedesArtifactId == artifact.ArtifactId);
}
