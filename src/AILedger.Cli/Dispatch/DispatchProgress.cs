using AILedger.Core.Contracts;
using AILedger.Cli.Providers;
using AILedger.Core.Domain;
using AILedger.Providers.Adapters;
namespace AILedger.Cli.Dispatch;

internal sealed class DispatchProgress(ProviderDispatchRequest request)
{
    public IReadOnlyList<HostHandoffReceipt> CognitiveReceipts { get; set; } = [];
    public bool CognitiveHandoffUnknown { get; set; }
    public AssurancePreparationStatus AssurancePreparation { get; set; }
    public int? TimeoutSeconds { get; set; }
    public DispatchPhase Phase { get; set; }
    public DispatchPhase? FaultPhase { get; set; }
    public AgentRunResult? ProviderResult { get; set; }
    public DispatchEventReceipt? Started { get; set; }
    public DispatchEventReceipt? Completed { get; set; }
    public LedgerRecordingStatus StartRecording { get; set; }
    public LedgerRecordingStatus CompletionRecording { get; set; }
    public ResultRetentionReceipt Retention { get; set; } = new(ResultRetentionStatus.NotAttempted);
    public GovernedArtifactKind? RequiredOutputKind { get; set; }
    public string? ManifestHash { get; set; }
    public int? ManifestArtifactCount { get; set; }
    private Exception? _error;
    private DispatchFailure? _failure;

    public static DispatchEventReceipt EventReceipt(CommandOutcome outcome, AgentRunStatus status) =>
        new(outcome.State.Version, outcome.Events.Select(item => item.EventId).ToArray(), status);

    public void Fail(Exception error)
    {
        _error = error is DispatchPreparationException preparation ? preparation.CompatibilityException : error;
        if (Started is null && StartRecording == LedgerRecordingStatus.Unknown && error is GovernanceException)
            StartRecording = LedgerRecordingStatus.Refused;
        var phase = FaultPhase ?? Phase;
        var kind = CompletionRecording is LedgerRecordingStatus.Unknown or LedgerRecordingStatus.Refused
            ? DispatchFailureKind.CompletionRecordingFailed
            : ProviderResult?.ProcessFailure is { CleanupConfirmed: false }
                ? DispatchFailureKind.ProcessCleanupUnconfirmed
            : error switch
            {
                DispatchPreparationException preparationError => preparationError.Kind,
                GovernanceException => DispatchFailureKind.Refused,
                CliUsageException or ArgumentException => DispatchFailureKind.InvalidRequest,
                PlatformNotSupportedException => DispatchFailureKind.PreparationUnsupported,
                OperationCanceledException => DispatchFailureKind.Cancelled,
                AgentAdapterException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception when phase is DispatchPhase.Preparation or DispatchPhase.ProviderProbe
                    => DispatchFailureKind.PreparationUnavailable,
                ProviderRunFailedException when ProviderResult is not null => ProviderFailureObservation.Classify(ProviderResult, RequiredOutputKind is not null),
                ProviderRunFailedException => DispatchFailureKind.ProviderOutcome,
                _ when phase == DispatchPhase.ProviderExecution => DispatchFailureKind.ProviderFault,
                _ => DispatchFailureKind.Unexpected
            };
        _failure = new(kind, error.Message, error is DispatchPreparationException configured ? configured.Code :
            error is GovernanceException refusal ? refusal.Kind.ToString() : RequiredOutputKind is not null ? "RequiredRunOutput" : null);
    }

    public ProviderDispatchResult Result() => new(request.TaskId, request.RunId, FaultPhase ?? Phase,
        ProviderResult, Started, Completed, StartRecording, CompletionRecording, Retention, _failure,
        ManifestHash, ManifestArtifactCount, RequiredOutputKind, AssurancePreparation, CognitiveHandoffUnknown, CognitiveReceipts) { Error = _error };
}
