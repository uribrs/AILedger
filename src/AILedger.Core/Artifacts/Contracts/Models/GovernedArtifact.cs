namespace AILedger.Core.Contracts;

public sealed record GovernedArtifact(
    ArtifactId ArtifactId,
    GovernedArtifactKind Kind,
    string Title,
    string Content,
    WorkItemId? WorkItemId,
    RunId? ProducerRunId,
    ArtifactId? SupersedesArtifactId,
    Provenance Provenance,
    AssuranceBinding? Assurance = null,
    IReadOnlyList<ArtifactMemberReplacement>? MemberReplacements = null);
