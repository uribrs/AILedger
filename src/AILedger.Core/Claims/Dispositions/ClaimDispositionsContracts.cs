using AILedger.Core.Contracts;
using AILedger.Core.Findings;

namespace AILedger.Core.ClaimDispositions;

public interface IClaimDispositionsRecorder
{
    Task<ClaimDispositionsResult> RecordClaimDispositionsAsync(ClaimDispositionsBinding binding,
        ClaimDispositionsRequest request, CancellationToken cancellationToken);
}

// Only trusted host configuration supplies attribution and the operation grant.
public sealed record ClaimDispositionsBinding(TaskId TaskId, ActorId ActorId, RunId? RunId,
    string CorrelationId, EventId? CausationId = null, bool AllowRecordClaimDispositions = false,
    bool AllowRunless = false);
public sealed record ClaimDispositionsRequest(int SchemaVersion, string RequestId,
    IReadOnlyList<ClaimDispositionInput> Dispositions);
public sealed record ClaimDispositionInput(string Key, ClaimReference Claim, ClaimStatus ExpectedStatus,
    ClaimStatus Status, string Rationale, IReadOnlyList<DispositionEvidenceReference> Evidence);
public sealed record ClaimReference(string ClaimId);
public sealed record DispositionEvidenceReference(string EvidenceId);
public sealed record ClaimDispositionChange(string Key, string ClaimId, ClaimStatus PreviousStatus,
    ClaimStatus Status, string Rationale, IReadOnlyList<DispositionEvidenceReference> Evidence,
    string EventId, IReadOnlyList<DispositionDependencyChange> Dependencies);
public sealed record DispositionDependencyChange(string EventId, string Kind, string Id, string Status);
public sealed record ClaimDispositionsReceipt(int SchemaVersion, string RequestId, string TransactionId,
    string TaskId, string ActorId, string? RunId, string CorrelationId, string? CausationId,
    string PayloadFingerprint, string FingerprintAlgorithm, DateTimeOffset CommittedAt,
    long LedgerVersion, IReadOnlyList<string> EventIds, IReadOnlyList<ClaimDispositionChange> Dispositions) : IRecordingReceiptIdentity;
public sealed record ClaimDispositionsResult(string AttemptId, ClaimDispositionsReceipt? Receipt, bool Replayed,
    FindingsError? Error, string CollectionStatus = "unavailable")
{
    public int SchemaVersion => 1;
    public string Status => Error is null ? "committed" : "error";
}
