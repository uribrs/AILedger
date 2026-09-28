using AILedger.Core.Contracts;
using AILedger.Core.Findings;

namespace AILedger.Core.Alternatives;

public interface IAlternativesRecorder
{
    Task<AlternativesResult> RecordAlternativesAsync(AlternativesBinding binding,
        AlternativesRequest request, CancellationToken cancellationToken);
}

// Host-owned, never deserialized from agent arguments. A grant does not confer capabilities.
public sealed record AlternativesBinding(TaskId TaskId, ActorId ActorId, RunId? RunId,
    string CorrelationId, EventId? CausationId = null, bool AllowRecordAlternatives = false,
    bool AllowRunless = false);
public sealed record AlternativesRequest(int SchemaVersion, string RequestId,
    IReadOnlyList<AlternativeInput> Alternatives);
public sealed record AlternativeInput(string Key, string Statement, string RejectionRationale,
    string? ReplacedByDecisionId = null, string? FromLesson = null);
public sealed record AlternativeMap(string Key, string AlternativeId, string EventId);
public sealed record AlternativesReceipt(int SchemaVersion, string RequestId, string TransactionId,
    string TaskId, string ActorId, string? RunId, string CorrelationId, string? CausationId,
    string PayloadFingerprint, string FingerprintAlgorithm, DateTimeOffset CommittedAt,
    long LedgerVersion, IReadOnlyList<string> EventIds, IReadOnlyList<AlternativeMap> Alternatives) : IRecordingReceiptIdentity;
public sealed record AlternativesResult(string AttemptId, AlternativesReceipt? Receipt, bool Replayed,
    FindingsError? Error, string CollectionStatus = "unavailable")
{
    public int SchemaVersion => 1;
    public string Status => Error is null ? "committed" : "error";
}
