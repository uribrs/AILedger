using System.Text;
using System.Text.Json;
using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.Episodes;
using AILedger.Core.Handoffs;
using AILedger.Core.Inspection;
using AILedger.Providers.Process;

namespace AILedger.Providers.Episodes;

public sealed partial class EpisodeExecutor(IEpisodeStore store, IEpisodeAuthoritySource authoritySource,
    ITaskInspector inspector, IProcessRunner runner, string scratchRoot, string hostVersion)
{
    public async Task<EpisodeInspection> ExecuteAsync(EpisodeStart start, bool resume, CancellationToken token)
    {
        var package = EpisodeExecutionValidation.Package(start);
        var authority = await authoritySource.ReadAsync(token).ConfigureAwait(false);
        var id = EpisodeExecutionValidation.ExecutionId(authority, start.RequestId);
        await using var lease = await store.AcquireAsync(id, token).ConfigureAwait(false);
        var records = await store.ReadAsync(id, token).ConfigureAwait(false);
        var admission = records.FirstOrDefault(r => r.Kind == "admitted") is { } admitted
            ? EpisodeExecutionValidation.Data<EpisodeAdmission>(admitted) : null;
        if (admission is not null && (EpisodeExecutionValidation.Hash(admission.Request) != EpisodeExecutionValidation.Hash(start) ||
            admission.AuthoritySha256 != EpisodeExecutionValidation.AuthorityHash(authority)))
            throw new ArgumentException("Execution key conflict: preserve the original package, principal and grant configuration.");
        if (admission is not null && !resume) return await InspectAsync(id, token).ConfigureAwait(false);
        if (Unfinished(records)) throw new ArgumentException("Unobserved provider ending. Inspect partial output and reconcile before resume.");
        if (records.Any(r => r.Kind == "finished")) return await InspectAsync(id, token).ConfigureAwait(false);

        try
        {
            await CheckAdmissionAsync(authority, start, package, token).ConfigureAwait(false);
            if (admission is null)
            {
                admission = new(id, authority.Principal, EpisodeExecutionValidation.AuthorityHash(authority), start,
                    DateTimeOffset.UtcNow.AddSeconds(authority.MaximumSeconds), hostVersion,
                    "offline-stdio-v1", authority.ExecutableSha256, EpisodeSandbox.EnvironmentIdentity, authority);
                await store.AppendAsync(id, "admitted", admission, token).ConfigureAwait(false);
            }
            CheckBudget(records, authority, admission);
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            await store.AppendAsync(id, "blocked", new { reason = e.Message }, CancellationToken.None).ConfigureAwait(false);
            return await InspectAsync(id, CancellationToken.None).ConfigureAwait(false);
        }
        await store.AppendAsync(id, "attempt", new { resume }, token).ConfigureAwait(false);
        await DispatchAsync(id, admission!, package, authority, token).ConfigureAwait(false);
        return await InspectAsync(id, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task CheckAdmissionAsync(EpisodeAuthority authority, EpisodeStart start, HandoffPackage package, CancellationToken token)
    {
        EpisodeExecutionValidation.Authority(authority, start, package);
        await EpisodeSandbox.ValidateAsync(authority, token).ConfigureAwait(false);
        var index = await new HandoffPreparer(inspector).IndexAsync(authority.Inspection,
            authority.LedgerRoot, package.Selection, package.Snapshot.LedgerVersion, token).ConfigureAwait(false);
        if (index.Records.Count != package.Inputs.Count || index.Records.Any(r => !package.Inputs.Any(i =>
            i.Reference.Kind == r.Kind && i.Reference.Id == r.Id && i.Reference.Sha256 == r.Sha256)))
            throw new ArgumentException("Stale input inventory; prepare and authorize a new package.");
    }

    private async Task<EpisodeAuthority> RevalidateAsync(EpisodeAdmission admission, HandoffPackage package, CancellationToken token)
    {
        var authority = await authoritySource.ReadAsync(token).ConfigureAwait(false);
        if (EpisodeExecutionValidation.AuthorityHash(authority) != admission.AuthoritySha256)
            throw new ArgumentException("Trusted authority changed; no implicit permission expansion or reduced-budget bypass.");
        await CheckAdmissionAsync(authority, admission.Request, package, token).ConfigureAwait(false);
        if (DateTimeOffset.UtcNow >= admission.Deadline) throw new ArgumentException("Elapsed budget exhausted.");
        return authority;
    }

    private static void CheckBudget(IReadOnlyList<EpisodeRecord> records, EpisodeAuthority authority, EpisodeAdmission admission)
    {
        if (DateTimeOffset.UtcNow >= admission.Deadline || records.Count(r => r.Kind == "attempt") >= authority.MaximumAttempts ||
            records.Count(r => r.Kind == "invocation") >= authority.MaximumInvocations)
            throw new ArgumentException("Elapsed, retry or invocation budget exhausted; output remains partial.");
    }

    private async Task DispatchAsync(string id, EpisodeAdmission admission, HandoffPackage package,
        EpisodeAuthority authority, CancellationToken token)
    {
        while (true)
        {
            var records = await store.ReadAsync(id, token).ConfigureAwait(false);
            if (records.Count(r => r.Kind == "invocation") >= authority.MaximumInvocations)
            {
                await store.AppendAsync(id, "blocked", new { reason = "Invocation budget exhausted; no further follow-up." }, token).ConfigureAwait(false);
                return;
            }
            var result = await InvokeAsync(id, admission, package, records, token).ConfigureAwait(false);
            if (result != "follow_up") return;
            // Only a successful, new, version-bound retrieval authorizes a further turn.
            // Provider prose cannot schedule work, change objectives or increase any limit.
        }
    }

    public async Task<EpisodeInspection> ReconcileAsync(string requestId, bool providerStopped, CancellationToken token)
    {
        var authority = await authoritySource.ReadAsync(token).ConfigureAwait(false);
        var id = EpisodeExecutionValidation.ExecutionId(authority, requestId);
        await using var lease = await store.AcquireAsync(id, token).ConfigureAwait(false);
        var records = await store.ReadAsync(id, token).ConfigureAwait(false);
        if (!providerStopped) throw new ArgumentException("Reconciliation requires the operator to confirm the old provider has stopped.");
        if (Unfinished(records))
        {
            var invocation = records.Last(r => r.Kind == "invocation").Data;
            var invocationId = invocation.GetProperty("invocation_id").GetString()!;
            await RecordInventoryAsync(id, invocationId, invocation.GetProperty("scratch_directory").GetString()!,
                invocation.GetProperty("initial_files").Deserialize<Dictionary<string, string>>(HandoffJson.Options)!, token).ConfigureAwait(false);
            if (!records.Any(r => r.Kind == "usage" && r.Data.GetProperty("invocation_id").GetString() == invocationId))
                await store.AppendAsync(id, "usage_unavailable", new { invocationId, status = "host_interrupted",
                    costUsd = (decimal?)null }, token).ConfigureAwait(false);
            await store.AppendAsync(id, "reconciled", new { outcome = "unknown", authority.Principal,
                providerStopped = "operator_attestation", reason = "Host did not observe process exit; committed partial submissions retained. No completion inferred." }, token).ConfigureAwait(false);
        }
        return await InspectAsync(id, token).ConfigureAwait(false);
    }

    public async Task<EpisodeInspection> InspectAsync(string id, CancellationToken token)
    {
        var records = await store.ReadAsync(id, token).ConfigureAwait(false);
        var unknown = Unfinished(records);
        var terminal = records.LastOrDefault(r => r.Kind is "process" or "blocked" or "reconciled" or "finished");
        var outcome = unknown ? "unknown" : terminal?.Kind switch
        {
            "process" => EpisodeExecutionValidation.Data<EpisodeProcessOutcome>(terminal).HostOutcome,
            "blocked" => "blocked", "reconciled" => "unknown", "finished" => "succeeded", _ => "not_started"
        };
        var result = records.LastOrDefault(r => r.Kind == "submission");
        var process = records.LastOrDefault(r => r.Kind == "process");
        var invocation = records.LastOrDefault(r => r.Kind == "invocation");
        var unobservedExit = invocation is not null && (process is null || process.Sequence < invocation.Sequence);
        var processOutcome = unknown || unobservedExit ? "unknown" : process is null ? "not_started" : EpisodeExecutionValidation.Data<EpisodeProcessOutcome>(process).Outcome;
        return new(id, outcome, processOutcome, result is null ? "none" : EpisodeExecutionValidation.Data<EpisodeStoredSubmission>(result).Result.Status,
            "not_assessed", unknown, records, await store.CountUncommittedAsync(id, token).ConfigureAwait(false));
    }

    private static bool Unfinished(IReadOnlyList<EpisodeRecord> records)
    {
        var invocation = records.LastOrDefault(r => r.Kind == "invocation");
        if (invocation is null) return false;
        var ending = records.LastOrDefault(r => r.Sequence > invocation.Sequence && r.Kind is "process" or "reconciled");
        return ending is null || (ending.Kind == "process" && EpisodeExecutionValidation.Data<EpisodeProcessOutcome>(ending).Outcome == "unknown");
    }
}
