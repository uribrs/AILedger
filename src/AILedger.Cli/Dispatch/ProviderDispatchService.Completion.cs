using AILedger.Cli.Providers;
using AILedger.Core.Contracts;
using AILedger.Core.Application;
using AILedger.Core.Domain;
using AILedger.Providers.Adapters;
namespace AILedger.Cli.Dispatch;
internal sealed partial class ProviderDispatchService
{
    private sealed record TerminalMeasurements(RunCost Cost, long? FirstWrite, string? Model,
        int? TruncatedLines, string? Failure, bool? EndedAtTimeout);

    private Task<long?> FirstWriteAsync(ActiveDispatch active) =>
        _runRecorder.MeasureFirstLedgerWriteAsync(service, active.Input, active.Start.RunId, active.StartedEvent.RecordedAt);

    private async Task CompleteAsync(ActiveDispatch active, DispatchProgress receipt, AgentRunStatus status,
        string? session, TerminalMeasurements measurements)
    {
        receipt.Phase = DispatchPhase.Completion;
        receipt.CompletionRecording = LedgerRecordingStatus.Unknown;
        try
        {
            var outcome = await _runRecorder.CompleteAsync(service, active.Input, active.Start.RunId, session, status,
                active.StartedEvent.EventId, active.Secret, measurements.Cost, measurements.FirstWrite, measurements.Model,
                measurements.TruncatedLines, receipt.TimeoutSeconds, measurements.Failure, measurements.EndedAtTimeout,
                receipt.ManifestHash, receipt.ManifestArtifactCount,
                _options.Checkpoint is { } checkpoint ? command => checkpoint.SaveAsync(active.Input, receipt.Result(), command, CancellationToken.None) : null).ConfigureAwait(false);
            receipt.Completed = DispatchProgress.EventReceipt(outcome, status);
            receipt.CompletionRecording = LedgerRecordingStatus.Recorded;
        }
        catch (GovernanceException)
        {
            receipt.CompletionRecording = LedgerRecordingStatus.Refused;
            throw;
        }
    }

    private async Task CloseFaultedLaunchAsync(ActiveDispatch active, DispatchProgress receipt, Exception error)
    {
        var refusal = error as GovernanceException ??
            (error as DispatchPreparationException)?.CompatibilityException as GovernanceException;
        if (refusal is not null)
            await JournalLaunchRefusalAsync(active.Input, active.Input.Mode, _options.LedgerRoot,
                active.Started.State.Version, refusal).ConfigureAwait(false);
        try
        {
            var status = error is OperationCanceledException ? AgentRunStatus.Cancelled : AgentRunStatus.Failed;
            // No provider result: no inferred cost, served model, drain count or timeout observation.
            await CompleteAsync(active, receipt, status, active.Input.SessionId,
                new(RunCost.Unmeasured, await FirstWriteAsync(active).ConfigureAwait(false), null, null,
                    error.Message, null)).ConfigureAwait(false);
        }
        catch (Exception cleanup)
        {
            throw new IOException("Provider launch failed and its active run could not be closed.",
                new AggregateException(error, cleanup));
        }
    }

    private async Task CloseProviderResultAsync(ActiveDispatch active, DispatchProgress receipt,
        AgentRunResult result, CancellationToken token)
    {
        var measurements = new TerminalMeasurements(RunCostReader.Read(result.Events),
            await FirstWriteAsync(active).ConfigureAwait(false), _runRecorder.ReadServedModel(result.Events),
            result.TruncatedLines, result.Failure, result.EndedAtTheLaunchTimeout);
        var requiredKind = active.Prepared.State.Roles[active.Input.Subject].Role switch
        {
            RoleKind.Verifier => GovernedArtifactKind.VerifierOutput,
            RoleKind.CodeReviewer => GovernedArtifactKind.CodeReviewOutput,
            _ => (GovernedArtifactKind?)null
        };
        try
        {
            await CompleteAsync(active, receipt, result.Status, result.ProviderSessionId, measurements).ConfigureAwait(false);
        }
        catch (GovernanceException refusal) when (result.Status == AgentRunStatus.Completed &&
            requiredKind is not null && refusal.Kind == GovernanceRefusalKind.RequiredRunOutput)
        {
            receipt.RequiredOutputKind = requiredKind;
            try
            {
                // Provider completion and kernel-required output are distinct. Keep provider facts
                // intact while recording the existing Failed fallback for missing applicable output.
                await CompleteAsync(active, receipt, AgentRunStatus.Failed, result.ProviderSessionId, measurements).ConfigureAwait(false);
            }
            catch (Exception cleanup) { throw CompletionFailure(result, new AggregateException(refusal, cleanup)); }
        }
        catch (Exception error) { throw CompletionFailure(result, error); }

        if (receipt.RequiredOutputKind is { } kind)
            throw new ProviderRunFailedException($"Provider run '{result.RunId}' did not record its required '{kind}' artifact; " +
                "its Ledger run was closed as 'Failed'.");
        if (result.Status == AgentRunStatus.Cancelled && token.IsCancellationRequested)
            throw new OperationCanceledException(token);
        if (result.Status != AgentRunStatus.Completed)
            throw new ProviderRunFailedException(ProviderFailureObservation.Diagnostic(result));
    }

    private static IOException CompletionFailure(AgentRunResult result, Exception cause) =>
        new($"Provider run '{result.RunId}' returned a terminal result, but its Ledger run could not be closed. " +
            "The provider result remains available in the dispatch receipt for recovery.", cause);
}
