using AILedger.Core.Contracts;

namespace AILedger.Core.Findings;

public interface IFindingsRecorder
{
    Task<FindingsResult> RecordAsync(FindingsBinding binding, FindingsRequest request,
        CancellationToken cancellationToken);
}

// Constructed by a trusted host, never deserialized from tool arguments. The recorder instance
// owns the ledger location. A grant admits this operation; it does not confer kernel capabilities.
public sealed record FindingsBinding(TaskId TaskId, ActorId ActorId, RunId? RunId,
    string CorrelationId, EventId? CausationId = null, bool AllowRecordFindings = false,
    bool AllowRunless = false);

public sealed record FindingsRequest(int SchemaVersion, string RequestId,
    IReadOnlyList<FindingInput> Findings, IReadOnlyList<EvidenceInput> Evidence);
public sealed record FindingInput(string Key, string Statement,
    string? ConsequenceIfWrong = null, string? FromLesson = null);
public sealed record EvidenceInput(string Key, string SourceType, string Citation, string Summary,
    IReadOnlyList<FindingReference> Supports, IReadOnlyList<FindingReference> Refutes);
public sealed record FindingReference(string? Finding = null, string? ClaimId = null);

public sealed record FindingMap(string Key, string ClaimId, string EventId);
public sealed record EvidenceMap(string Key, string EvidenceId, string EventId);
public sealed record FindingsReceipt(int SchemaVersion, string RequestId, string TransactionId,
    string TaskId, string ActorId, string? RunId, string CorrelationId, string? CausationId,
    string PayloadFingerprint, string FingerprintAlgorithm, DateTimeOffset CommittedAt,
    long LedgerVersion, IReadOnlyList<string> EventIds, IReadOnlyList<FindingMap> Findings,
    IReadOnlyList<EvidenceMap> Evidence);

public sealed record FindingsError(string Code, string Message, string Boundary, string CommitState,
    string Retry, string? ItemPath = null, string? RuleId = null);

// The transport maps this discriminated result to the task-1 success/error wire envelopes.
// CollectionStatus is application observation, not part of the immutable receipt or wire v1.
public sealed record FindingsResult(string AttemptId, FindingsReceipt? Receipt, bool Replayed,
    FindingsError? Error, string CollectionStatus = "unavailable")
{
    public int SchemaVersion => 1;
    public string Status => Error is null ? "committed" : "error";
}
