namespace AILedger.Core.Contracts;

public sealed record ArtifactMemberReplacement(
    WorkItemId WorkItemId,
    IReadOnlyList<ArtifactId> ArtifactIds);
