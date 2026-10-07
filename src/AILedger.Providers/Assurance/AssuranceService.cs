using System.Text.Json;
using AILedger.Core.Assurance;
using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.Episodes;
using AILedger.Core.Handoffs;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Providers.Assurance;

// Composes the task-12 journal/lease, task-8 content identities and existing process runner.
// No governed command, invented run, stage transition or completion is produced here.
public sealed partial class AssuranceService : IAssuranceService
{
    private readonly IEpisodeStore _store;
    private readonly IAssurancePolicySource _source;
    private readonly AssurancePolicy _initial;
    private readonly AssuranceSession _session;
    private readonly IProcessRunner _runner;
    private readonly Func<CancellationToken, Task> _authorizeSession;
    private readonly IGovernedAssuranceContext? _governedContext;
    private readonly AsyncLocal<IGovernedAssuranceLease?> _governedLease = new();
    private readonly string _id;
    private readonly string _policyHash;
    public string Role => Principal(_initial).Role;
    public AssuranceSession Session => _session;
    public IReadOnlyList<string> AuthorizedInputPaths() => AssuranceConfiguration.InputPaths(_initial, _session.Principal);
    public IReadOnlyList<AssuranceArea> ConfiguredAreas => AssuranceConfiguration.Areas(_initial, _session.Principal);

    public async Task ValidateConfigurationAsync(CancellationToken token)
    {
        var current = await AuthorizeAsync(token).ConfigureAwait(false);
        await AssuranceConfiguration.ValidateInputsAsync(current, _session.Principal, token).ConfigureAwait(false);
    }
    public IReadOnlyList<string> Operations { get; }

    public AssuranceService(IEpisodeStore store, IAssurancePolicySource source, AssurancePolicy policy,
        AssuranceSession session, IProcessRunner runner, Func<CancellationToken, Task>? authorizeSession = null,
        IGovernedAssuranceContext? governedContext = null)
    {
        Policy(policy); Text(session.Principal, "principal", 128); Text(session.SessionId, "session_id", 128);
        Text(session.Provider, "provider", 128);
        _store = store; _source = source; _initial = policy; _session = session; _runner = runner;
        _authorizeSession = authorizeSession ?? (_ => Task.CompletedTask);
        _governedContext = governedContext;
        _policyHash = PolicyIdentity(policy); _id = Hash(new { Namespace = "assurance-v1", policy.CaseId });
        var principal = Principal(policy);
        Operations = principal.Role == "acceptance" ? ["inspect_assurance", "accept_assurance"] :
            principal.Role == "verification" ? ["inspect_assurance", "read_assurance", "run_assurance_checks", "record_assurance"] :
            ["inspect_assurance", "read_assurance", "record_assurance"];
    }

    public async Task<AssuranceResponse> InvokeAsync(string operation, JsonElement request, CancellationToken token)
    {
        var attempt = "AA_" + Guid.NewGuid().ToString("N");
        try
        {
            Require(Operations.Contains(operation), "authorization_denied", "This host session has no grant for that operation.");
            await _authorizeSession(token).ConfigureAwait(false);
            Require(_initial.Governed is null || _governedContext is not null, "missing_governed_host", "Governed policy requires an associated trusted ledger host.");
            await using var governed = _governedContext is null ? null : await _governedContext.AcquireAsync(_initial, _session, token).ConfigureAwait(false);
            _governedLease.Value = governed;
            await using var lease = await _store.AcquireAsync(_id, token).ConfigureAwait(false);
            var policy = await AuthorizeAsync(token).ConfigureAwait(false);
            var records = await _store.ReadAsync(_id, token).ConfigureAwait(false);
            var entries = Entries(records);
            var result = operation switch
            {
                "inspect_assurance" => await InspectAsync(Parse<InspectAssuranceRequest>(request), policy, records, entries, attempt, token).ConfigureAwait(false),
                "read_assurance" => await ReadAsync(Parse<ReadAssuranceRequest>(request), policy, entries, attempt, token).ConfigureAwait(false),
                "run_assurance_checks" => await ChecksAsync(Parse<RunAssuranceChecksRequest>(request), policy, records, entries, attempt, token).ConfigureAwait(false),
                "record_assurance" => await RecordAsync(Parse<RecordAssuranceRequest>(request), policy, entries, attempt, token).ConfigureAwait(false),
                "accept_assurance" => await AcceptAsync(Parse<AcceptAssuranceRequest>(request), policy, entries, attempt, token).ConfigureAwait(false),
                _ => throw new AssuranceRefusal("unsupported", "Unknown assurance operation.")
            };
            await ObserveAsync(attempt, operation, request, result.Status, result.Receipt?.Id).ConfigureAwait(false);
            return result;
        }
        catch (AILedger.Core.Domain.GovernanceException e)
        { return await FailureAsync(attempt, operation, request, "governed_admission", e.Message, "Resolve the owning governed prerequisite.", "not_committed").ConfigureAwait(false); }
        catch (AssuranceRefusal e) { return await FailureAsync(attempt, operation, request, e.Code, e.Message, e.Recovery, "not_committed").ConfigureAwait(false); }
        catch (Exception e) when (e is ArgumentException or JsonException or InvalidOperationException or NullReferenceException)
        { return await FailureAsync(attempt, operation, request, "invalid_request", e.Message, "Use the advertised typed request; inspect current IDs and coverage.", "unknown").ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException)
        { return await FailureAsync(attempt, operation, request, "outcome_unknown", e.Message, "Inspect, then retry the exact request/key/session. Pending checks require explicit operator reconciliation.", "unknown").ConfigureAwait(false); }
    }

    private async Task<AssuranceResponse> FailureAsync(string attempt, string operation, JsonElement request,
        string code, string message, string recovery, string commit)
    {
        await ObserveAsync(attempt, operation, request, code, null).ConfigureAwait(false);
        return new("error", attempt, null, false, null, new(code, message, recovery, commit));
    }
    private async Task ObserveAsync(string attempt, string operation, JsonElement request, string status, string? receipt)
    {
        // Diagnostic attempts have their own lease and population. Failure cannot revoke a receipt.
        try
        {
            var telemetry = Hash(new { Namespace = "assurance-attempts-v1", _initial.CaseId });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await using var lease = await _store.AcquireAsync(telemetry, timeout.Token).ConfigureAwait(false);
            await _store.AppendAsync(telemetry, "assurance_attempt", new { attempt, operation, request_sha256 = Hash(request),
                _session, status, receipt, provider_usage = (object?)null }, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException) { }
    }
    private AssurancePrincipal Principal(AssurancePolicy policy) => AssuranceConfiguration.Principal(policy, _session.Principal);
    private async Task<AssurancePolicy> AuthorizeAsync(CancellationToken token)
    {
        if (_governedLease.Value is { } governed) await governed.RevalidateAsync(token).ConfigureAwait(false);
        else await _authorizeSession(token).ConfigureAwait(false);
        var current = await _source.ReadAsync(token).ConfigureAwait(false); Policy(current);
        Require(PolicyIdentity(current) == _policyHash, "authority_changed", "Policy identity changed; restore the original policy for recovery or open a new host session for the new policy.");
        var principal = Principal(current);
        AssuranceConfiguration.EnsureEnabled(principal);
        return current;
    }
    private void Scope(AssurancePolicy policy, string area) =>
        AssuranceConfiguration.EnsureAreaGranted(policy, _session.Principal, area);
    private async Task<AssuranceSnapshot> CurrentAsync(AssurancePolicy policy, string area, string? expected, CancellationToken token)
    {
        Scope(policy, area);
        CheckDependencyScope(policy, area, new HashSet<string>(StringComparer.Ordinal));
        var snapshot = await AssuranceSnapshotReader.CaptureAsync(policy, area, token).ConfigureAwait(false);
        if (policy.Governed is { } scope)
        {
            var state = _governedLease.Value?.State ?? throw new AssuranceRefusal("missing_governed_host", "No live governed association.");
            var candidate = await AssuranceConfiguration.CapturePolicyBindingIdentityAsync(policy, token).ConfigureAwait(false);
            var basis = GovernedAssuranceRules.Capture(state, scope, candidate);
            basis = basis with { AuthoritySha256 = Hash(new { basis.AuthoritySha256, Policy = PolicyIdentity(policy) }) };
            snapshot = BindGovernedSnapshot(snapshot, basis);
        }
        if (expected is not null) Require(snapshot.BindingSha256 == expected, "stale_candidate", "Candidate, requirements or a relied-on source/dependency changed. Inspect the current binding and reassess affected assurance.");
        return snapshot;
    }
    private static AssuranceSnapshot BindGovernedSnapshot(AssuranceSnapshot snapshot, GovernedAssuranceBasis basis) => snapshot with
    {
        PhysicalBindingSha256 = snapshot.BindingSha256,
        Governed = basis, BindingSha256 = Hash(new { snapshot.BindingSha256, Governed = basis }),
        Dependencies = snapshot.Dependencies.ToDictionary(d => d.Key,
            d => Hash(new { BindingSha256 = d.Value, Governed = basis }), StringComparer.Ordinal)
    };

    private void CheckDependencyScope(AssurancePolicy policy, string area, HashSet<string> visited)
    {
        if (!visited.Add(area)) return;
        foreach (var dependency in policy.Areas.Single(a => a.Id == area).DependsOnAreas)
        {
            Scope(policy, dependency); CheckDependencyScope(policy, dependency, visited);
        }
    }
    private static List<AssuranceEntry> Entries(IReadOnlyList<EpisodeRecord> records)
    {
        var entries = records.Where(r => r.Kind == "assurance_entry").Select(r => r.Data.Deserialize<AssuranceEntry>(HandoffJson.Options)
            ?? throw new InvalidDataException("Missing assurance entry.")).ToList();
        foreach (var entry in entries)
        {
            var json = EpisodeExecutionValidation.Serialize(entry.Payload);
            if (entry.Receipt.ContentSha256 != ArtifactSubmissionIdentity.ContentHash(json) || entry.Receipt.ContentBytes != System.Text.Encoding.UTF8.GetByteCount(json))
                throw new InvalidDataException("Assurance receipt/content mismatch.");
        }
        return entries;
    }
    private async Task<AssuranceResponse?> ReplayAsync<T>(string operation, string key, T request, List<AssuranceEntry> entries, string attempt, CancellationToken token)
    {
        Text(key, "request_id", 128);
        var existing = entries.SingleOrDefault(e => e.Receipt.Operation == operation && e.Receipt.Principal == _session.Principal && e.Receipt.RequestId == key);
        if (existing is null) return null;
        Require(existing.Receipt.RequestSha256 == Fingerprint(operation, request), "request_conflict", "The key was committed with different content, session or authority. Retry the original request on its original binding.");
        await _store.AppendAsync(_id, "assurance_retry", new { receipt_id = existing.Receipt.Id, attempt }, token).ConfigureAwait(false);
        return new("recorded", attempt, existing.Receipt, true, existing.Payload, null);
    }
    private string Fingerprint<T>(string operation, T request) => Hash(new { operation, request, _session, policy = _policyHash });
    private async Task<AssuranceResponse> CommitAsync<T>(string operation, string key, T request, object payload,
        AssuranceSnapshot snapshot, string attempt, CancellationToken token)
    {
        var current = await AuthorizeAsync(token).ConfigureAwait(false);
        await CurrentAsync(current, snapshot.AreaId, snapshot.BindingSha256, token).ConfigureAwait(false);
        if (payload is AssuranceAcceptance acceptance)
        {
            var records = await _store.ReadAsync(_id, token).ConfigureAwait(false);
            var entries = Entries(records);
            Require(!PendingChecks(records, entries).Any(), "incomplete_assurance", "Interrupted checks remain unresolved.");
            ValidateAcceptance(acceptance.Decision, snapshot, current, entries, _session.Principal);
        }
        var content = Json(payload);
        var json = EpisodeExecutionValidation.Serialize(content);
        Require(System.Text.Encoding.UTF8.GetByteCount(json) <= 256 * 1024, "capacity_exceeded", "Assurance payload exceeds 256 KiB; narrow the area or report. Nothing was silently truncated.");
        var id = "AU_" + Guid.NewGuid().ToString("N");
        var receipt = new AssuranceReceipt(id, operation, key, Fingerprint(operation, request),
            _session.Principal, _session.SessionId, _session.Provider, _session.RequestedModel, _policyHash, ScopePolicyIdentity(current, snapshot.AreaId),
            ArtifactSubmissionIdentity.ContentHash(json), System.Text.Encoding.UTF8.GetByteCount(json),
            "ailedger-assurance:" + _id + ":" + id, DateTimeOffset.UtcNow);
        var entry = new AssuranceEntry(receipt, content);
        await _store.AppendAsync(_id, "assurance_entry", entry, token).ConfigureAwait(false);
        return new("recorded", attempt, receipt, false, entry.Payload, null);
    }
    private static T Payload<T>(AssuranceEntry entry) where T : class => entry.Payload.Deserialize<T>(HandoffJson.Options)
        ?? throw new InvalidDataException("Missing assurance payload.");
    // A committed payload names inputs by path and digest. Content is returned only where a read asked for it,
    // so one area's bytes are not repeated in every receipt and a read stays inside the payload cap.
    private static AssuranceSnapshot Identity(AssuranceSnapshot snapshot) =>
        snapshot with { Inputs = snapshot.Inputs.Select(i => i with { Content = "" }).ToArray() };
    private static AssuranceSnapshot Snapshot(AssuranceEntry entry) => entry.Payload.GetProperty("snapshot").Deserialize<AssuranceSnapshot>(HandoffJson.Options)
        ?? throw new InvalidDataException("Missing assurance snapshot.");
}
