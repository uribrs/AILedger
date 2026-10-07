using System.Text.Json;
using AILedger.Storage;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
namespace AILedger.Cli.Dispatch;

internal static class DispatchRecovery
{
    internal static async Task<ProviderDispatchResult?> ReconcileAsync(IGovernedTaskService service,
        ProviderDispatchRequest input, ProviderDispatchResult? receipt, CompleteRunCommand? completion, CancellationToken token)
    {
        var state = await service.GetStateAsync(input.TaskId, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("Task unavailable during dispatch reconciliation.");
        if (!state.Runs.TryGetValue(input.RunId, out var run)) return null;
        if (run.ActorId != input.Subject || run.Provider != input.Provider || run.WorkItemId != input.WorkItemId)
            throw new GovernanceException("Recovered run does not match original dispatch binding.");
        if (run.Status == AgentRunStatus.Active && completion is not null)
        {
            // Hard restart loses the process-local launcher credential. Preserve the terminal
            // observation, but never mint a credential or impersonate the original launcher.
            throw new GovernanceException("Retained terminal observation requires the original live launcher, or trusted termination confirmation and governed orphan closure. No credential is persisted or replayed.");
        }
        if (run.Status == AgentRunStatus.Active || receipt is null)
            throw new GovernanceException("Provider/check execution is uncertain. A lease timeout is not termination; reconcile the original host before further work.");
        if (receipt.Retention.Status != ResultRetentionStatus.Retained || receipt.Retention.Path is not { } path || !File.Exists(path))
            throw new GovernanceException("Recovered dispatch has no retained result; preserve the unresolved outcome.");
        if (new FileInfo(path).Length > 64 * 1024 * 1024 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Invalid retained result file.");
        var retained = JsonSerializer.Deserialize<AgentRunResult>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), LedgerJson.CreateOptions());
        if (retained is null || retained.RunId != input.RunId || retained.Provider != input.Provider ||
            receipt.ProviderResult is { } observed && JsonSerializer.Serialize(observed, LedgerJson.CreateOptions()) != JsonSerializer.Serialize(retained, LedgerJson.CreateOptions()))
            throw new InvalidDataException("Retained result identity/content contradicts its dispatch receipt.");
        if (completion is not null && run.Status != completion.Status)
            throw new GovernanceException("Recovered closure contradicts the retained terminal command.");
        var events = new List<EventId>();
        await foreach (var row in service.GetHistoryAsync(input.TaskId, token).ConfigureAwait(false))
            if (row.Data is RunCompleted completed && completed.RunId == input.RunId) events.Add(row.EventId);
        return receipt with { ProviderResult = retained, Completed = new(state.Version, events, run.Status), CompletionRecording = LedgerRecordingStatus.Recorded,
            Failure = run.Status == AgentRunStatus.Completed ? null : receipt.Failure ?? new(
                ProviderFailureObservation.Classify(retained, receipt.MissingRequiredOutput is not null), ProviderFailureObservation.Diagnostic(retained)) };
    }
}
