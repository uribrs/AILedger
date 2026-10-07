using AILedger.Cli.Dispatch;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
namespace AILedger.Cli.Orchestration;

/// <summary>Trusted host acknowledgment that all execution for an orphan has stopped.
/// Never exposed to cognitive sessions. Does not close a run or manufacture a provider result.</summary>
public static class TrustedExecutionRecovery
{
    public static async Task<ProviderDispatchResult> ConfirmStoppedAsync(FileGovernedTaskService host,
        TaskId task, ActorId actor, RunId runId, EvidenceId terminationEvidence, CancellationToken token)
    {
        var lease = await host.AcquireCoordinationAsync(task, Guid.NewGuid().ToString("N"), TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
        try
        {
            var bound = host.BindCoordination(lease);
            var state = await bound.GetStateAsync(task, token).ConfigureAwait(false) ?? throw new InvalidDataException("Task unavailable.");
            if (!state.Roles.TryGetValue(actor, out var role) || role.Role != RoleKind.Operator ||
                !state.Runs.TryGetValue(runId, out var run) || run.Status is not (AgentRunStatus.Failed or AgentRunStatus.Cancelled))
                throw new GovernanceException("Trusted stopped-execution recovery requires an operator and an already failed or cancelled orphan.");
            if (!state.Evidence.TryGetValue(terminationEvidence, out var evidence) || evidence.Provenance.ActorId != actor ||
                evidence.TrustedTerminationVersion != 1 || evidence.SourceType != "process-termination" || evidence.Citation != $"{task.Value}/{runId.Value}" ||
                evidence.Provenance.RecordedAt < run.StartedAt)
                throw new GovernanceException("Recovery needs operator-authored process-termination evidence for this task/run, recorded after launch. Confirm the provider tree and any check processes have stopped.");
            var continuity = new DriverContinuity(bound, lease);
            await continuity.LoadAsync(token).ConfigureAwait(false);
            var intent = continuity.Journal.Intents.SingleOrDefault(i => i.Request.RunId == runId)
                ?? throw new GovernanceException("No original dispatch intent exists for this run.");
            if (intent.Request.Subject != run.ActorId || intent.Request.Provider != run.Provider || intent.Request.WorkItemId != run.WorkItemId)
                throw new GovernanceException("Orphan identity differs from its original dispatch intent.");
            var events = new List<EventId>();
            await foreach (var row in bound.GetHistoryAsync(task, token).ConfigureAwait(false))
                if (row.Data is RunCompleted completed && completed.RunId == runId) events.Add(row.EventId);
            var receipt = intent.Receipt ?? new(task, runId, DispatchPhase.Completion, null, null, null,
                LedgerRecordingStatus.Recorded, LedgerRecordingStatus.Unknown, new(ResultRetentionStatus.NotAttempted),
                null, null, null, null, AssurancePreparationStatus.NotConfigured);
            receipt = receipt with { ProviderResult = null, Completed = new(state.Version, events, run.Status),
                CompletionRecording = LedgerRecordingStatus.Recorded,
                Failure = new(DispatchFailureKind.ProviderOutcome, $"Execution stopped by trusted confirmation {terminationEvidence.Value}; provider result remains unavailable or untrusted.", "trusted_execution_stopped") };
            await continuity.RecordStoppedAsync(intent, receipt, token).ConfigureAwait(false);
            return receipt;
        }
        finally { await host.ReleaseCoordinationAsync(lease, CancellationToken.None).ConfigureAwait(false); }
    }
}
