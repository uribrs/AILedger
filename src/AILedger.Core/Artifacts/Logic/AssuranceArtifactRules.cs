using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Runs;

namespace AILedger.Core.Artifacts;

internal static class AssuranceArtifactRules
{
    internal static IReadOnlyList<ArtifactMemberReplacement> Replacements(
        GovernedTaskState state, GovernedArtifactKind kind, IReadOnlyList<WorkItemId> members) =>
        members.Select(member => new ArtifactMemberReplacement(member,
            ArtifactApplicability.Current(state).Where(artifact => artifact.Kind == kind &&
                ArtifactApplicability.CurrentMembers(state, artifact).Contains(member))
                .Select(artifact => artifact.ArtifactId).OrderBy(id => id.Value, StringComparer.Ordinal).ToArray())).ToArray();

    internal static void Validate(GovernedTaskState state, GovernedArtifact artifact)
    {
        var binding = artifact.Assurance!;
        var members = AssuranceRules.ValidateShape(artifact.WorkItemId, binding);
        if (artifact.ProducerRunId is not { } producer || !state.Runs.TryGetValue(producer, out var run) ||
            run.Assurance is null || artifact.WorkItemId != run.WorkItemId || !AssuranceRules.Same(binding, run.Assurance))
            throw new GovernanceException("Assurance artifact coverage: must inherit its producer's exact assurance binding and anchor.");
        if (artifact.Kind is not (GovernedArtifactKind.VerifierOutput or GovernedArtifactKind.CodeReviewOutput))
            throw new GovernanceException("Assurance artifact coverage: only assurance outputs may inherit coverage.");
        if (artifact.SupersedesArtifactId is not null)
            throw new GovernanceException("Assurance artifact coverage: replacement edges must not globally supersede an artifact.");
        var expected = Replacements(state, artifact.Kind, members);
        var actual = artifact.MemberReplacements;
        if (actual is null || actual.Count != expected.Count || actual.Any(row => row is null) ||
            !actual.Select(row => row.WorkItemId).SequenceEqual(members) ||
            actual.Where((row, index) => row.ArtifactIds is null || !row.ArtifactIds.SequenceEqual(expected[index].ArtifactIds)).Any())
            throw new GovernanceException("Assurance artifact coverage: replacement rows must equal current per-member heads.");
    }
}
