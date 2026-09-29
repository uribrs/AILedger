using System.Text.Json;
using AILedger.Core.Handoffs;
using AILedger.Core.Inspection;

namespace AILedger.Core.Episodes;

// Trusted operator configuration, never a provider argument. Enabled may be revoked in place.
// Every other field is pinned by the admission receipt. No ambient actor or default grant.
public sealed record EpisodeAuthority(int SchemaVersion, string Principal, bool Enabled,
    DateTimeOffset ExpiresAt, string PackageSha256, string LedgerRoot, InspectionBinding Inspection,
    IReadOnlyList<EpisodeGrant> Grants, bool AllowSubmitResult, string Executable,
    string ExecutableSha256, int MaximumInvocations, int MaximumAttempts, int MaximumSeconds,
    IReadOnlyList<EpisodeSourceIdentity> Sources);
public sealed record EpisodeSourceIdentity(string Key, string Version, string Sha256);
public sealed record EpisodeStart(int SchemaVersion, string RequestId, PreparedHandoff Handoff);
public sealed record EpisodeSubmission(int SchemaVersion, string RequestId, EpisodeResult Result);
public sealed record EpisodeSubmissionReceipt(string SubmissionId, string RequestId, string Principal,
    string ExecutionId, string InvocationId, string ContentSha256, int ContentBytes,
    string ContentReference, DateTimeOffset RecordedAt);
public sealed record EpisodeUsage(string? SessionId, string? Model, long? InputTokens,
    long? OutputTokens, decimal? CostUsd);
// Provider protocol is deliberately closed. Each frame must have exactly its matching payload.
public sealed record EpisodeFrame(int SchemaVersion, string Operation, EpisodeSubmission? Submission = null,
    RetrievalQuery? Retrieval = null, EpisodeUsage? Usage = null);
public sealed record EpisodeRecord(int SchemaVersion, int Sequence, string PreviousSha256,
    DateTimeOffset RecordedAt, string Kind, JsonElement Data);
public sealed record EpisodeRecordEnvelope(string Sha256, string RecordJson);
public sealed record EpisodeAdmission(string ExecutionId, string Principal, string AuthoritySha256,
    EpisodeStart Request, DateTimeOffset Deadline, string HostVersion, string Provider,
    string ExecutableSha256, string Environment, EpisodeAuthority Authority);
public sealed record EpisodeProcessOutcome(string InvocationId, string Outcome, int? ExitCode,
    string Reason, DateTimeOffset StartedAt, DateTimeOffset EndedAt, int? TruncatedLines, string HostOutcome);
public sealed record EpisodeStoredSubmission(EpisodeSubmissionReceipt Receipt, EpisodeResult Result);
public sealed record EpisodeToolAttempt(string AttemptId, string InvocationId, string Operation,
    string RequestSha256, string Status, string? Detail, string? SubmissionId,
    RetrievalResult? Retrieval = null, string? QuerySha256 = null);
public sealed record EpisodeFileChange(string Path, string? BeforeSha256, string? AfterSha256);
public sealed record EpisodeInspection(string ExecutionId, string ExecutionOutcome, string ProcessOutcome, string AuthoredStatus,
    string Acceptance, bool ReconciliationRequired, IReadOnlyList<EpisodeRecord> Records,
    int UncommittedFiles);

public interface IEpisodeStore
{
    Task<IAsyncDisposable> AcquireAsync(string executionId, CancellationToken token);
    Task<IReadOnlyList<EpisodeRecord>> ReadAsync(string executionId, CancellationToken token);
    Task<EpisodeRecord> AppendAsync(string executionId, string kind, object data, CancellationToken token);
    Task<int> CountUncommittedAsync(string executionId, CancellationToken token);
}
public interface IEpisodeAuthoritySource
{
    Task<EpisodeAuthority> ReadAsync(CancellationToken token);
}
