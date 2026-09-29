using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Episodes;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Providers.Assurance;

public sealed partial class AssuranceService
{
    private async Task<AssuranceResponse> ChecksAsync(RunAssuranceChecksRequest request, AssurancePolicy policy,
        IReadOnlyList<EpisodeRecord> records, List<AssuranceEntry> entries, string attempt, CancellationToken token)
    {
        Request(request.SchemaVersion, request.RequestId); Scope(policy, request.AreaId);
        if (await ReplayAsync("run_assurance_checks", request.RequestId, request, entries, attempt, token).ConfigureAwait(false) is { } replay) return replay;
        Require(AILedger.Core.Artifacts.ArtifactSubmissionIdentity.IsHash(request.ExpectedBinding), "invalid_request", "expected_binding must be the exact SHA-256 returned by inspection.");
        var snapshot = await CurrentAsync(policy, request.AreaId, request.ExpectedBinding, token).ConfigureAwait(false);
        Unique(request.CheckIds, "check_ids"); Require(request.CheckIds.Count is >= 1 and <= 8, "invalid_request", "Run 1–8 configured checks.");
        Require(request.CheckIds.All(id => snapshot.Criteria.Any(c => c.CheckId == id)), "authorization_denied", "Only this area's configured checks may run.");
        Require(request.CheckIds.Sum(id => policy.Checks.Single(c => c.Id == id).TimeoutSeconds) <= 300, "capacity_exceeded", "A check batch is limited to 300 seconds of configured timeouts.");
        Require(_session.Principal != policy.Implementer, "self_approval", "Independent checks require a principal other than the implementer.");
        var fingerprint = Fingerprint("run_assurance_checks", request);
        var started = records.FirstOrDefault(r => r.Kind == "assurance_check_started" && r.Data.GetProperty("principal").GetString() == _session.Principal && r.Data.GetProperty("request_id").GetString() == request.RequestId);
        if (started is not null)
        {
            Require(started.Data.GetProperty("fingerprint").GetString() == fingerprint, "request_conflict", "A check attempt already reserved this key with another body or binding.");
            throw new AssuranceRefusal("inspection_interrupted", "A host check was reserved without a committed receipt. It is unknown and will not rerun automatically.",
                "Inspect the recorded attempt. The operator must stop any remaining process and reconcile it before requesting new checks with a new key.");
        }
        Require(!PendingChecks(records, entries).Any(), "inspection_interrupted", "A prior host check has an unknown ending. Reconcile it before executing another check.");
        Require(records.Count(r => r.Kind == "assurance_check_started") < 8, "capacity_exceeded", "This bounded assurance case has used its eight check batches; obtain a new bounded intent rather than extending it silently.");
        var directory = Directory.CreateTempSubdirectory("ailedger-assurance-check-").FullName;
        var inputs = await MaterializeAsync(policy, snapshot, directory, token).ConfigureAwait(false);
        await _store.AppendAsync(_id, "assurance_check_started", new { fingerprint, request_id = request.RequestId,
            principal = _session.Principal, session_id = _session.SessionId, area_id = request.AreaId,
            snapshot.BindingSha256, directory, request.CheckIds, process_outcome = "unknown" }, token).ConfigureAwait(false);
        var results = new List<AssuranceTestResult>();
        foreach (var id in request.CheckIds)
        {
            await AuthorizeAsync(token).ConfigureAwait(false);
            var result = await RunCheckAsync(policy.Checks.Single(c => c.Id == id), directory, inputs, token).ConfigureAwait(false);
            results.Add(result);
            // Host observations survive revocation/cancellation or failure before the operation receipt.
            await _store.AppendAsync(_id, "assurance_test_observed", new { fingerprint, test = result }, CancellationToken.None).ConfigureAwait(false);
            if (result.Outcome != "succeeded") break;
        }
        foreach (var id in request.CheckIds.Except(results.Select(r => r.CheckId), StringComparer.Ordinal))
            results.Add(new(id, "not_checked", null, "", "", null, RuntimeInformation.OSDescription, "", [], DateTimeOffset.UtcNow, null, "A preceding check did not succeed."));
        return await CommitAsync("run_assurance_checks", request.RequestId, request,
            new AssuranceTestBatch(snapshot, results), snapshot, attempt, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<List<AssuranceInput>> MaterializeAsync(AssurancePolicy policy, AssuranceSnapshot snapshot,
        string directory, CancellationToken token)
    {
        var inputs = new Dictionary<string, AssuranceInput>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        async Task AddAsync(AssuranceSnapshot current)
        {
            if (!visited.Add(current.AreaId)) return;
            foreach (var input in current.Inputs)
            {
                if (inputs.TryGetValue(input.Path, out var old)) Require(old.Sha256 == input.Sha256, "stale_candidate", "Input changed during snapshot composition.");
                inputs[input.Path] = input;
            }
            foreach (var dependency in current.Dependencies)
            {
                var child = await AssuranceSnapshotReader.CaptureAsync(policy, dependency.Key, token).ConfigureAwait(false);
                Require(child.BindingSha256 == dependency.Value, "stale_candidate", "Dependency changed during test materialization.");
                await AddAsync(child).ConfigureAwait(false);
            }
        }
        await AddAsync(snapshot).ConfigureAwait(false);
        foreach (var input in inputs.Values)
        {
            var path = Path.Combine(directory, input.Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, input.Content, new UTF8Encoding(false), token).ConfigureAwait(false);
        }
        return inputs.Values.ToList();
    }

    private async Task<AssuranceTestResult> RunCheckAsync(AssuranceCheck check, string directory,
        IReadOnlyList<AssuranceInput> inputs, CancellationToken token)
    {
        var start = DateTimeOffset.UtcNow; var output = new StringBuilder(); var error = new StringBuilder();
        var outputGate = new object(); var truncated = false; var tally = new TruncatedLineTally();
        ValueTask Capture(StringBuilder target, string line)
        {
            lock (outputGate)
            {
                if (output.Length + error.Length + line.Length + 1 > 16384) truncated = true;
                else target.AppendLine(line);
            }
            return ValueTask.CompletedTask;
        }
        try
        {
            Require(await FileHashAsync(check.Executable, token).ConfigureAwait(false) == check.ExecutableSha256,
                "executable_changed", "Configured check executable digest changed.");
            var exit = await _runner.RunAsync(new(check.Executable, directory, check.Arguments, "",
                new Dictionary<string, string> { ["HOME"] = directory, ["TMPDIR"] = directory }, TimeSpan.FromSeconds(check.TimeoutSeconds)),
                (line, _) => Capture(output, line), (line, _) => Capture(error, line), tally, token).ConfigureAwait(false);
            var intact = await InputsIntactAsync(directory, inputs, token).ConfigureAwait(false);
            var executableIntact = await FileHashAsync(check.Executable, token).ConfigureAwait(false) == check.ExecutableSha256;
            var limitation = !intact ? "Captured input bytes changed during checks." : !executableIntact ? "Executable changed during checks." :
                truncated || exit.TruncatedLines is not 0 ? "Output capture was truncated or its coverage is unknown." : null;
            return new(check.Id, exit.ExitCode == 0 && limitation is null ? "succeeded" : "failed", exit.ExitCode,
                output.ToString(), error.ToString(), exit.TruncatedLines, EnvironmentDescription(directory), check.ExecutableSha256,
                check.Arguments, exit.StartedAt, exit.EndedAt, limitation);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException or AssuranceRefusal or System.ComponentModel.Win32Exception)
        {
            return new(check.Id, "unknown", null, output.ToString(), error.ToString(), tally.Observed,
                EnvironmentDescription(directory), check.ExecutableSha256, check.Arguments, start, null, e.Message);
        }
    }
    private static string EnvironmentDescription(string directory) => RuntimeInformation.OSDescription + "; " + RuntimeInformation.FrameworkDescription +
        "; cwd=" + directory + "; captured UTF-8 inputs; system runtime dependencies are not hermetic; no model process";
    private static async Task<string> FileHashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
    }
    private static async Task<bool> InputsIntactAsync(string directory, IReadOnlyList<AssuranceInput> inputs, CancellationToken token)
    {
        foreach (var input in inputs)
        {
            var path = Path.Combine(directory, input.Path);
            if (new FileInfo(path).LinkTarget is not null || !File.Exists(path) || await FileHashAsync(path, token).ConfigureAwait(false) != input.Sha256) return false;
        }
        return true;
    }

    // Operator adapter only: this is an attestation, never an inferred process ending or a tool grant.
    public async Task ReconcileChecksAsync(string requestId, string checkPrincipal, bool confirmedStopped, CancellationToken token)
    {
        Require(confirmedStopped, "confirmation_required", "Confirm that the recorded check process has stopped before reconciling.");
        await using var lease = await _store.AcquireAsync(_id, token).ConfigureAwait(false);
        var policy = await AuthorizeAsync(token).ConfigureAwait(false);
        Require(Principal(policy).Role == "acceptance", "authorization_denied", "Only the explicit accepting authority can attest reconciliation.");
        var records = await _store.ReadAsync(_id, token).ConfigureAwait(false); var entries = Entries(records);
        var pending = PendingChecks(records, entries).SingleOrDefault(r => r.GetProperty("request_id").GetString() == requestId && r.GetProperty("principal").GetString() == checkPrincipal);
        Require(pending.ValueKind != JsonValueKind.Undefined, "invalid_reference", "No matching unknown check attempt remains.");
        Scope(policy, pending.GetProperty("area_id").GetString()!);
        await _store.AppendAsync(_id, "assurance_check_reconciled", new { fingerprint = pending.GetProperty("fingerprint").GetString(),
            _session, outcome = "unknown", basis = "operator attests process stopped; no successful ending invented" }, token).ConfigureAwait(false);
    }
}
