using System.Text.Json;
using AILedger.Core.Contracts;

namespace AILedger.Core.Findings;

// Observations are not truth. Counts describe readable, distinct observations, never a census
// of calls. Provider usage belongs to the retrospective's existing run population only.
public sealed record FindingsMeasurementReport(
    long CanonicalVersion,
    int? CommittedTransactions,
    int? GranularEvents,
    int? ObservedTransportAttempts,
    int? ObservedToolAttempts,
    int? ObservedApplicationAttempts,
    IReadOnlyList<FindingsReceipt> Transactions,
    IReadOnlyList<FindingsAttemptJoin> Attempts,
    IReadOnlyList<FindingsRunObservation> Runs,
    IReadOnlyList<FindingsCoverageGap> CoverageGaps,
    IReadOnlyList<string> NotMeasured);

public sealed record FindingsCoverageGap(string Source, string Reason, long? Line = null);

public sealed record FindingsAttemptJoin(
    string Source, long Line, string Kind, FindingsAttemptObservation Observation,
    string? CanonicalTransactionId, string? JoinedApplicationAttemptId,
    string? JoinedRunId, string? JoinedProviderSessionId);

// Completion is kept once per run, never copied onto attempts or transactions. This also keeps
// served Model separate from a requested model and session at start separate from an observation.
public sealed record FindingsRunObservation(
    string RunId, string ActorId, string Provider, string? ProviderVersion,
    string? RequestedModel, RunCompleted? Completion);

// The two v1 journals share fields but have distinct identities and populations. Nullable fields
// remain nullable; the reader validates required members before deserialization.
public sealed record FindingsAttemptObservation
{
    public int SchemaVersion { get; init; }
    public string? AttemptId { get; init; }
    public string? TransportAttemptId { get; init; }
    public string? TransportSessionId { get; init; }
    public string? MessageKind { get; init; }
    public string? RequestId { get; init; }
    public string? PayloadFingerprint { get; init; }
    public string? TaskId { get; init; }
    public string? ActorId { get; init; }
    public string? RunId { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? Provider { get; init; }
    public string? ProviderSessionId { get; init; }
    public JsonElement? KernelIdentity { get; init; }
    public JsonElement? AdapterIdentity { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public double? DurationMs { get; init; }
    public double? LockWaitMs { get; init; }
    public double? ValidationMs { get; init; }
    public double? AppendMs { get; init; }
    public bool? ApplicationEntered { get; init; }
    public string? ApplicationAttemptId { get; init; }
    public double? ApplicationMs { get; init; }
    public string? ApplicationCollectionStatus { get; init; }
    public string? Outcome { get; init; }
    public string? Code { get; init; }
    public string? Boundary { get; init; }
    public string? CommitState { get; init; }
    public string? TransactionId { get; init; }
    public IReadOnlyList<string>? EventIds { get; init; }
    public bool? Replayed { get; init; }
    public string? TransportFailure { get; init; }
    public string? ResponseDelivery { get; init; }
    public string? CollectionStatus { get; init; }
}

public sealed record LocatedFindingsAttempt(string Source, long Line, string Kind,
    FindingsAttemptObservation Observation);
