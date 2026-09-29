using AILedger.Core.Contracts;
using AILedger.Core.Findings;

namespace AILedger.Core.Artifacts;

public interface IArtifactSubmitter
{
    Task<ArtifactSubmissionResult> SubmitArtifactAsync(ArtifactSubmissionBinding binding,
        ArtifactSubmissionRequest request, CancellationToken cancellationToken);
}

// Only the trusted host constructs this binding. Recording never confers endorsement authority.
public sealed record ArtifactSubmissionBinding(TaskId TaskId, ActorId ActorId, RunId RunId,
    string CorrelationId, EventId? CausationId = null, bool AllowSubmitArtifact = false);
public sealed record ArtifactSubmissionRequest(int SchemaVersion, string RequestId,
    GovernedArtifactKind Kind, string Title, string Content, string? ExpectedContentSha256 = null,
    string? SupersedesArtifactId = null);
public sealed record SubmittedWorkVersion(string WorkItemId, string WorkingRunId);
public sealed record SubmittedReplacement(string WorkItemId, IReadOnlyList<string> ArtifactIds);
public sealed record SubmittedArtifact(string ArtifactId, string Kind, string Title, string MediaType,
    string ContentSha256, int ContentBytes, string ContentReference, string? WorkItemId,
    string ProducerRunId, string? SupersedesArtifactId, IReadOnlyList<string> CoveredWorkItemIds,
    string? CandidateId, string? VerifierRunId, IReadOnlyList<SubmittedWorkVersion> WorkVersions,
    IReadOnlyList<SubmittedReplacement> MemberReplacements);
public sealed record ArtifactSubmissionReceipt(int SchemaVersion, string RequestId, string TransactionId,
    string TaskId, string ActorId, string RunId, string CorrelationId, string? CausationId,
    string PayloadFingerprint, string FingerprintAlgorithm, DateTimeOffset CommittedAt,
    long LedgerVersion, IReadOnlyList<string> EventIds, SubmittedArtifact Artifact) : IRecordingReceiptIdentity;
public sealed record ArtifactSubmissionResult(string AttemptId, ArtifactSubmissionReceipt? Receipt, bool Replayed,
    FindingsError? Error, string CollectionStatus = "unavailable")
{
    public int SchemaVersion => 1;
    public string Status => Error is null ? "committed" : "error";
}
