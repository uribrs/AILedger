using System.Text.Json.Serialization;
using AILedger.Core.Contracts;

namespace AILedger.Cli.Dispatch;

public sealed record ProviderDispatchRequest(TaskId TaskId, ActorId ActorId, RunId RunId, string Provider)
{
    public string? RoutingCandidate { get; init; }
    public DriverRecoveryObservation? Recovery { get; init; }
    public long? ExpectedVersion { get; init; }
    public ActorId? SubjectActorId { get; init; }
    public WorkItemId? WorkItemId { get; init; }
    public AgentLaunchMode Mode { get; init; } = AgentLaunchMode.New;
    public string? SessionId { get; init; }
    public CognitiveWorkKind? CognitiveWork { get; init; }
    public string? Model { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyList<string> AdditionalDirectories { get; init; } = [];
    public IReadOnlyList<WorkItemId> AdditionalWork { get; init; } = [];
    public string? Candidate { get; init; }
    public RunId? VerifierRun { get; init; }
    public int TimeoutSeconds { get; init; } = 1800;
    public int MaximumContextBytes { get; init; } = 256 * 1024;
    public EventId? Cause { get; init; }
    public string Correlation { get; init; } = Guid.NewGuid().ToString("N");
    public CoordinatorSessionId? CoordinatorSession { get; init; }
    // Human CLI compatibility only. Routine authority rejects both before adapter resolution.
    public string? WithoutBriefReason { get; init; }
    public EvidenceId? StaleBriefEvidence { get; init; }
    public ActorId Subject => SubjectActorId ?? ActorId;
}

/// <summary>Selected by trusted host composition, never supplied by a provider child.</summary>
public sealed record DispatchHostOptions(string LedgerRoot, string LessonRoot)
{
    internal string? PreparationProfiles { get; init; }
    internal IDispatchCheckpoint? Checkpoint { get; init; }
    public IReadOnlyList<string> ProtectedPaths { get; init; } = [];
    public string? CognitiveRoot { get; init; }
    public string? Executable { get; init; }
    public string? OutputSchema { get; init; }
    public string? AssuranceAuthority { get; init; }
    public string? AssuranceStore { get; init; }
}

public interface IProviderDispatchService
{
    Task<ProviderDispatchResult> LaunchAsync(ProviderDispatchRequest request, CancellationToken cancellationToken);
}

public enum DispatchPhase { Preparation, ProviderProbe, RunAdmission, BriefDelivery, ProviderExecution, ResultRetention, Completion }
public enum DispatchFailureKind { Refused, InvalidRequest, PreparationUnavailable, PreparationUnsupported, ProviderFault, ProviderOutcome, Cancelled, CompletionRecordingFailed, Unexpected, ProviderTransport, ProviderStartup, ProcessCleanupUnconfirmed }
public enum AssurancePreparationStatus { NotConfigured, Requested, Prepared, Opened }
public enum ResultRetentionStatus { NotAttempted, Retained, Failed }
public enum LedgerRecordingStatus { NotAttempted, Recorded, Refused, Unknown }
public sealed record DispatchEventReceipt(long TaskVersion, IReadOnlyList<EventId> EventIds, AgentRunStatus Status);
public sealed record ResultRetentionReceipt(ResultRetentionStatus Status, string? Path = null, string? Diagnostic = null);
public sealed record DispatchFailure(DispatchFailureKind Kind, string Diagnostic, string? Code = null);

/// <summary>Provider facts, acknowledged ledger writes and retention are independent observations.
/// Unknown writes must be reconciled before retry. This is not a durable dispatch reservation.</summary>
public sealed record ProviderDispatchResult(
    TaskId TaskId, RunId RunId, DispatchPhase Phase,
    AgentRunResult? ProviderResult, DispatchEventReceipt? Started, DispatchEventReceipt? Completed,
    LedgerRecordingStatus StartRecording, LedgerRecordingStatus CompletionRecording,
    ResultRetentionReceipt Retention, DispatchFailure? Failure,
    string? DeliveredManifestHash, int? DeliveredManifestArtifactCount, GovernedArtifactKind? MissingRequiredOutput,
    AssurancePreparationStatus AssurancePreparation, bool CognitiveHandoffUnknown = false,
    IReadOnlyList<HostHandoffReceipt>? CognitiveReceipts = null)
{
    // Preserves local CLI exception/exit compatibility; routing uses typed facts above.
    [JsonIgnore] public Exception? Error { get; init; }
}
