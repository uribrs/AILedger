using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AILedger.Cli.Dispatch;
using AILedger.Core.Authority;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
namespace AILedger.Cli.Orchestration;

internal sealed record DispatchIntent(string Basis, TaskStage Stage, ProviderDispatchRequest Request,
    Provenance Assignment, ProviderDispatchResult? Receipt = null, CompleteRunCommand? Completion = null,
    string? ResultBasis = null);
internal sealed record DriverJournal(int SchemaVersion, IReadOnlyList<DispatchIntent> Intents, DriverResult? Stop = null);

internal sealed class DriverContinuity(FileGovernedTaskService host, CoordinationLease lease) : IDispatchCheckpoint
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DriverJournal _journal = new(1, []);
    private readonly JsonSerializerOptions _json = LedgerJson.CreateOptions();
    internal DriverJournal Journal => _journal;

    internal async Task LoadAsync(CancellationToken token)
    {
        var snapshot = await host.ReadCoordinationAsync(lease, token).ConfigureAwait(false);
        _journal = snapshot.Payload is null ? new(1, []) : JsonSerializer.Deserialize<DriverJournal>(snapshot.Payload, _json)
            ?? throw new InvalidDataException("Empty driver journal.");
        if (_journal.SchemaVersion != 1 || _journal.Intents.Count > 256)
            throw new InvalidDataException("Unsupported or oversized driver journal; no dispatch.");
    }

    internal async Task<DispatchIntent> BeginAsync(ProviderDispatchRequest input, GovernedTaskState state, CancellationToken token)
    {
        var basis = Basis(state, input.Candidate ?? input.RoutingCandidate);
        var pending = _journal.Intents.LastOrDefault(i => i.Receipt is null || i.Receipt.Completed is null &&
            (i.Receipt.StartRecording == LedgerRecordingStatus.Unknown || i.Receipt.Started is not null));
        if (pending is not null)
        {
            if (pending.Stage != state.Stage || pending.Request.CognitiveWork != input.CognitiveWork ||
                pending.Request.WorkItemId != input.WorkItemId || pending.Request.Candidate != input.Candidate || pending.Request.RoutingCandidate != input.RoutingCandidate)
                throw new GovernanceException("Unreconciled dispatch blocks a different action.");
            return pending;
        }
        var prior = _journal.Intents.LastOrDefault(i => i.Stage == state.Stage && i.Request.CognitiveWork == input.CognitiveWork &&
            i.Request.WorkItemId == input.WorkItemId && (i.Basis == basis || i.ResultBasis == basis));
        if (prior is not null)
        {
            if (IsInfrastructureFailure(prior.Receipt)) return prior;
            if (prior.Receipt is { Failure: null, Retention.Status: ResultRetentionStatus.Retained }) return prior;
            if (prior.Receipt?.Completed?.Status == AgentRunStatus.Completed && prior.ResultBasis == basis)
                return prior;
            throw new GovernanceException("Unchanged refused or stalled action; inspect retained diagnostic and repair its prerequisites before redispatch.");
        }
        if (_journal.Intents.Count >= 256) throw new GovernanceException("Dispatch budget exhausted; work remains unresolved.");
        var intent = new DispatchIntent(basis, state.Stage, input, state.Roles[input.Subject].AssignedBy);
        await ChangeAsync(j => j with { Intents = j.Intents.Append(intent).ToArray(), Stop = null }, token).ConfigureAwait(false);
        return intent;
    }

    internal static bool IsInfrastructureFailure(ProviderDispatchResult? receipt) => receipt?.Failure?.Kind is
        DispatchFailureKind.ProviderFault or DispatchFailureKind.ProviderTransport or DispatchFailureKind.ProviderStartup or
        DispatchFailureKind.ProcessCleanupUnconfirmed or DispatchFailureKind.PreparationUnavailable or DispatchFailureKind.PreparationUnsupported;

    public async Task SaveAsync(ProviderDispatchRequest input, ProviderDispatchResult receipt, CompleteRunCommand? completion, CancellationToken token)
    {
        var state = await host.GetStateAsync(input.TaskId, token).ConfigureAwait(false) ?? throw new InvalidDataException("Task unavailable.");
        await ChangeAsync(j => j with { Intents = j.Intents.Select(i => i.Request.RunId == input.RunId
            ? i with { Receipt = receipt with { ProviderResult = null }, Completion = completion is null ? i.Completion : completion with { LaunchToken = null }, ResultBasis = Basis(state, input.Candidate ?? input.RoutingCandidate) } : i).ToArray() }, token).ConfigureAwait(false);
    }

    internal Task RecordStoppedAsync(DispatchIntent intent, ProviderDispatchResult receipt, CancellationToken token) =>
        ChangeAsync(j => j with { Intents = j.Intents.Select(i => i.Request.RunId == intent.Request.RunId
            ? i with { Receipt = receipt, Completion = null, ResultBasis = i.Basis } : i).ToArray(), Stop = null }, token);

    internal Task StopAsync(DriverResult result, CancellationToken token) => ChangeAsync(j => j with { Stop = result with { Dispatches = result.Dispatches.Select(d => d with { ProviderResult = null }).ToArray() } }, token);

    internal async Task RenewAsync(TimeSpan lifetime, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { await host.RenewCoordinationAsync(lease, lifetime, token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task ChangeAsync(Func<DriverJournal, DriverJournal> change, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var current = await host.ReadCoordinationAsync(lease, token).ConfigureAwait(false);
            var next = change(_journal);
            await host.SaveCoordinationAsync(lease, current.Revision, JsonSerializer.Serialize(next, _json), token).ConfigureAwait(false);
            _journal = next;
        }
        finally { _gate.Release(); }
    }

    // Ignore run IDs, heartbeat/brief events and timestamps. Repeating the same observation is not progress.
    internal static string Basis(GovernedTaskState state, string? candidate) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { state.Stage, candidate, state.Claims, state.Decisions, state.Constraints,
            state.WorkItems, state.Challenges, state.Escalations, state.Evidence,
            artifacts = ArtifactApplicability.Current(state).Where(a => a.Kind is GovernedArtifactKind.UserRequest or
                GovernedArtifactKind.InternalRecon or GovernedArtifactKind.PromptContract or GovernedArtifactKind.OrchestrationPlan)
                .OrderBy(a => a.ArtifactId.Value).ToArray() }, LedgerJson.CreateOptions()))));
}

internal sealed class DurableDispatch(IProviderDispatchService dispatch, IGovernedTaskService restricted,
    DriverContinuity continuity) : IProviderDispatchService
{
    public async Task<ProviderDispatchResult> LaunchAsync(ProviderDispatchRequest request, CancellationToken token)
    {
        var stopped = continuity.Journal.Intents.LastOrDefault(i => i.Receipt?.Started is not null &&
            DriverContinuity.IsInfrastructureFailure(i.Receipt));
        if (stopped is not null) return stopped.Receipt!;
        var state = await restricted.GetStateAsync(request.TaskId, token).ConfigureAwait(false) ?? throw new InvalidDataException("Task unavailable.");
        request = request with { ExpectedVersion = state.Version };
        var intent = await continuity.BeginAsync(request, state, token).ConfigureAwait(false);
        if (intent.Assignment != state.Roles[request.Subject].AssignedBy || intent.Request.Subject != request.Subject ||
            intent.Request.Provider != request.Provider || intent.Request.Model != request.Model)
            throw new GovernanceException("Recovered judgment's authority/profile changed; it cannot be consumed.");
        // Return the original failure facts, including old faults without retained output. This
        // is a stop, never a fresh judgment or process-termination claim. Findings cannot repair
        // an infrastructure failure by starting the same failed provider again.
        if (DriverContinuity.IsInfrastructureFailure(intent.Receipt)) return intent.Receipt!;
        if (state.Runs.ContainsKey(intent.Request.RunId))
        {
            if (intent.ResultBasis is { } basis && basis != DriverContinuity.Basis(state, request.Candidate ?? request.RoutingCandidate))
                throw new GovernanceException("Recovered judgment has a changed evidence, scope or candidate basis; bounded reassessment is required.");
            var recovered = await DispatchRecovery.ReconcileAsync(restricted, intent.Request, intent.Receipt, intent.Completion, token).ConfigureAwait(false);
            if (recovered is not null) return recovered;
        }
        // No run exists under the task lock, and old owners are fenced at admission. Reuse the original identity.
        return await dispatch.LaunchAsync(intent.Request with { ExpectedVersion = state.Version }, token).ConfigureAwait(false);
    }
}
