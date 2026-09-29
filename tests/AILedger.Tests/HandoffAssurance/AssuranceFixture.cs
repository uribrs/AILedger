using System.Security.Cryptography;
using System.Text.Json;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Episodes;
using AILedger.Core.Handoffs;
using AILedger.Providers.Assurance;
using AILedger.Storage.Episodes;
using AILedger.Tests.Support;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Tests.HandoffAssurance;

internal sealed class AssuranceFixture : IDisposable, IAssurancePolicySource
{
    private readonly TemporaryDirectory _directory = new();
    internal string Root => _directory.Path;
    internal AssurancePolicy Policy { get; set; } = null!;
    internal IEpisodeStore Store { get; set; } = null!;
    internal ScriptedCheckRunner Runner { get; } = new();
    internal Dictionary<string, AssuranceService> Sessions { get; } = new();
    internal static async Task<AssuranceFixture> CreateAsync()
    {
        var f = new AssuranceFixture();
        foreach (var area in new[] { "a", "b", "c" })
        {
            await File.WriteAllTextAsync(Path.Combine(f.Root, area + ".cs"), "return 1;\n");
            await File.WriteAllTextAsync(Path.Combine(f.Root, area + ".txt"), "Return one, without changing other behavior.\n");
            await File.WriteAllTextAsync(Path.Combine(f.Root, area + ".source"), "Supported runtime contract v1.\n");
        }
        var executable = Path.Combine(f.Root, "test-runner"); await File.WriteAllTextAsync(executable, "scripted runner bytes");
        var sha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable))).ToLowerInvariant();
        f.Policy = new(1, "disposable-assurance", f.Root, "implementer",
            new[] { "a", "b", "c" }.Select(id => new AssuranceArea(id, [id + ".cs"], [id + ".txt"], [id + ".source"],
                id == "b" ? ["a"] : [], [new("behavior", "Returns one", "unit")])).ToArray(),
            new[] { ("reviewer", "review"), ("verifier", "verification"), ("synthesizer", "synthesis"), ("operator", "acceptance"), ("implementer", "review") }
                .Select(p => new AssurancePrincipal(p.Item1, p.Item2, true, DateTimeOffset.UtcNow.AddHours(1), ["a", "b", "c"])).ToArray(),
            [new("unit", executable, sha, [], 5)]);
        f.Store = new FileEpisodeStore(Path.Combine(f.Root, "store"));
        f.OpenSessions(); return f;
    }
    internal void OpenSessions()
    {
        Sessions.Clear();
        foreach (var p in Policy.Principals) Sessions.Add(p.Id, new(Store, this, Policy, new(p.Id, p.Id + "-session", "scripted", null), Runner));
    }
    public Task<AssurancePolicy> ReadAsync(CancellationToken token) => Task.FromResult(Policy);
    internal Task<AssuranceResponse> CallAsync(string principal, string operation, object request) => Sessions[principal].InvokeAsync(operation, Json(request), default);
    internal async Task<AssuranceInspection> InspectAsync(string principal = "operator", string area = "a")
    {
        var result = await CallAsync(principal, "inspect_assurance", new InspectAssuranceRequest(1, area));
        Assert.Null(result.Error); return result.Data!.Value.Deserialize<AssuranceInspection>(HandoffJson.Options)!;
    }
    internal async Task<(RecordAssuranceRequest Request, AssuranceResponse Response)> ReportAsync(string principal,
        string area = "a", string status = "complete", string checkStatus = "pass", string? key = null, string? supersedes = null,
        IReadOnlyList<AssuranceFinding>? findings = null)
    {
        key ??= Guid.NewGuid().ToString("N");
        var snapshot = (await InspectAsync(principal, area)).Snapshot;
        var paths = snapshot.Inputs.Select(i => i.Path).ToArray();
        var read = await CallAsync(principal, "read_assurance", new ReadAssuranceRequest(1, key + "-read", area, snapshot.BindingSha256, paths));
        Assert.Null(read.Error);
        var tests = new List<string>();
        if (principal == "verifier" && checkStatus == "pass")
        {
            var check = await CallAsync(principal, "run_assurance_checks", new RunAssuranceChecksRequest(1, key + "-tests", area, snapshot.BindingSha256, ["unit"]));
            Assert.Null(check.Error); tests.Add(check.Receipt!.Id);
        }
        var request = new RecordAssuranceRequest(1, key, area, snapshot.BindingSha256, status, "Independent scripted fixture finding",
            paths, [read.Receipt!.Id], [new("behavior", checkStatus, "Actual captured inputs and check result", tests)], findings ?? [], [], supersedes);
        return (request, await CallAsync(principal, "record_assurance", request));
    }
    internal async Task<AssuranceResponse> AcceptAsync(IEnumerable<string> reports, IReadOnlyList<AssuranceDisposition>? dispositions = null, string area = "a")
    {
        var snapshot = (await InspectAsync(area: area)).Snapshot;
        return await CallAsync("operator", "accept_assurance", new AcceptAssuranceRequest(1, Guid.NewGuid().ToString("N"), area,
            snapshot.BindingSha256, reports.ToArray(), "Explicit bounded fixture acceptance based on independent evidence", dispositions ?? []));
    }
    public void Dispose() => _directory.Dispose();
}

internal sealed class HostDeath : Exception;
internal sealed class ScriptedCheckRunner : IProcessRunner
{
    internal int Calls { get; private set; }
    internal string Outcome { get; set; } = "success";
    internal Action<ProcessInvocation>? BeforeExit { get; set; }
    public async Task<ProcessExit> RunAsync(ProcessInvocation invocation, Func<string, CancellationToken, ValueTask> stdout,
        Func<string, CancellationToken, ValueTask> stderr, TruncatedLineTally? tally, CancellationToken token)
    {
        Calls++; var start = DateTimeOffset.UtcNow;
        Assert.NotEqual(Path.GetTempPath(), invocation.WorkingDirectory);
        await stdout("Scripted observed check output", token);
        BeforeExit?.Invoke(invocation);
        if (Outcome == "crash") throw new HostDeath();
        if (Outcome == "cancelled") throw new OperationCanceledException();
        if (Outcome == "missing") throw new IOException("Required test environment unavailable.");
        if (Outcome == "timeout") throw new ProviderProcessTimeoutException(invocation.Timeout);
        return new(Outcome == "failure" ? 1 : 0, start, DateTimeOffset.UtcNow, Outcome == "truncated" ? 1 : 0);
    }
}
