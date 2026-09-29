using System.Text.Json;
using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.Episodes;
using AILedger.Core.Handoffs;
using AILedger.Providers.Episodes;
using AILedger.Storage.Episodes;
using AILedger.Tests.Findings;
using AILedger.Tests.Handoffs;
using AILedger.Tests.Inspection;

namespace AILedger.Tests.Episodes;

public sealed class EpisodeExecutionTests
{
    [Fact]
    public async Task SuccessKeepsAuthorshipProcessAndAcceptanceSeparateAndExactRetryDoesNotLaunch()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = await Fixture.CreateAsync();
        f.Runner.Lines = [f.Submit("final", "reported_complete")];
        var result = await f.RunAsync();
        Assert.Equal("succeeded", result.ExecutionOutcome); Assert.Equal("reported_complete", result.AuthoredStatus);
        Assert.Equal("not_assessed", result.Acceptance);
        Assert.Single(result.Records.Where(r => r.Kind == "submission"));
        Assert.Single(result.Records.Where(r => r.Kind == "usage_unavailable"));
        Assert.Single(result.Records.Where(r => r.Kind == "inventory"));
        var replay = await f.RunAsync();
        Assert.Equal(result.Records.Count, replay.Records.Count); Assert.Equal(1, f.Runner.Calls);
        Assert.Equal(f.Before, await InspectionTests.Files(f.Ledger.Directory));
    }

    [Theory]
    [InlineData("cancelled")] [InlineData("failed")] [InlineData("blocked")] [InlineData("crash")]
    public async Task InterruptionRetainsPartialAndResumeReplaysSubmissionWithoutDuplicates(string ending)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = await Fixture.CreateAsync();
        f.Runner.Lines = [f.Submit("checkpoint", "partial")];
        f.Runner.Ending = ending;
        EpisodeInspection interrupted;
        if (ending == "crash")
        {
            await Assert.ThrowsAsync<SimulatedHostDeath>(() => f.RunAsync());
            interrupted = await f.Executor.InspectAsync(f.Id, default);
            Assert.True(interrupted.ReconciliationRequired); Assert.Equal("unknown", interrupted.ExecutionOutcome);
            await Assert.ThrowsAsync<ArgumentException>(() => f.RunAsync(true));
            await Assert.ThrowsAsync<ArgumentException>(() => f.Executor.ReconcileAsync(f.Start.RequestId, false, default));
            var reconciled = await f.Executor.ReconcileAsync(f.Start.RequestId, true, default);
            Assert.Equal("unknown", reconciled.ProcessOutcome);
            Assert.False(reconciled.ReconciliationRequired);
        }
        else { interrupted = await f.RunAsync(); Assert.Equal(ending, interrupted.ExecutionOutcome); }
        Assert.Equal("partial", interrupted.AuthoredStatus);
        var original = EpisodeExecutionValidation.Data<EpisodeStoredSubmission>(interrupted.Records.Single(r => r.Kind == "submission"));
        f.Runner.Ending = "success";
        f.Runner.Lines = [f.Submit("checkpoint", "partial"), f.Submit("final", "reported_complete")];
        var resumed = await f.RunAsync(true);
        Assert.Equal("succeeded", resumed.ExecutionOutcome);
        var outputs = resumed.Records.Where(r => r.Kind == "submission").Select(EpisodeExecutionValidation.Data<EpisodeStoredSubmission>).ToArray();
        Assert.Equal(2, outputs.Length); Assert.Equal(original.Receipt, outputs[0].Receipt);
        Assert.Contains("checkpoint", f.Runner.LastInput);
    }

    [Theory]
    [InlineData("revoked")] [InlineData("missing-grant")] [InlineData("expired")]
    [InlineData("package")] [InlineData("executable")] [InlineData("stale")]
    [InlineData("spend")] [InlineData("budget")] [InlineData("source")]
    public async Task AdmissionRefusesUnavailableAuthorityIdentityFreshnessAndResources(string change)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = await Fixture.CreateAsync();
        f.Authority.Current = change switch
        {
            "revoked" => f.Authority.Current with { Enabled = false },
            "missing-grant" => f.Authority.Current with { Grants = [] },
            "expired" => f.Authority.Current with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) },
            "package" => f.Authority.Current with { PackageSha256 = new('0', 64) },
            "executable" => f.Authority.Current with { ExecutableSha256 = new('0', 64) },
            "budget" => f.Authority.Current with { MaximumInvocations = 0 },
            _ => f.Authority.Current
        };
        if (change == "stale") await f.Ledger.ExecuteAsync(new AddClaimCommand(f.Ledger.Actor, null, "changed", new("changed"), "New source", null));
        if (change is "spend" or "source")
        {
            var p = f.Package;
            p = change == "spend" ? p with { Spec = p.Spec with { Budget = p.Spec.Budget with { MaximumCostUsd = 1 } } } :
                p with { Sources = [new("source", "file@v1", "v1", ArtifactSubmissionIdentity.ContentHash("body"), "body", true, "needed", "loss", "now")] };
            p = p with { SpecSha256 = EpisodeExecutionValidation.Hash(p.Spec) };
            f.Start = f.Start with { Handoff = HandoffJson.Seal(p) };
            f.Authority.Current = f.Authority.Current with { PackageSha256 = f.Start.Handoff.PackageSha256 };
        }
        if (change == "spend") await Assert.ThrowsAsync<ArgumentException>(() => f.RunAsync());
        else Assert.Equal("blocked", (await f.RunAsync()).ExecutionOutcome);
        Assert.Equal(0, f.Runner.Calls);
    }

    [Fact]
    public async Task RevocationDuringRunKeepsRawPartialButRefusesCanonicalSubmission()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = await Fixture.CreateAsync();
        f.Runner.Lines = [f.Submit("partial", "partial")];
        f.Runner.BeforeOutput = () => f.Authority.Current = f.Authority.Current with { Enabled = false };
        var result = await f.RunAsync();
        Assert.Equal("blocked", result.ExecutionOutcome); Assert.Equal("none", result.AuthoredStatus);
        Assert.Single(result.Records.Where(r => r.Kind == "provider_output"));
        Assert.Contains(result.Records, r => r.Kind == "tool" && r.Data.GetProperty("status").GetString() == "refused");
    }

    [Fact]
    public async Task RetrievalUsesExistingInspectorAndOnlyNewRetrievalDispatchesFollowUp()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = await Fixture.CreateAsync();
        var query = f.Package.Inputs[0].Reference.Retrieve;
        f.Runner.Lines = [EpisodeExecutionValidation.Serialize(new EpisodeFrame(1, "retrieve_context", Retrieval: query))];
        f.Runner.OnInvocation = call => { if (call == 2) f.Runner.Lines = [f.Submit("final", "reported_complete")]; };
        var result = await f.RunAsync();
        Assert.Equal("succeeded", result.ExecutionOutcome); Assert.Equal(2, f.Runner.Calls);
        Assert.Single(result.Records.Where(r => r.Kind == "read_reserved"));
        Assert.Contains("retrieval", f.Runner.LastInput);
    }

    [Theory]
    [InlineData("repeated")] [InlineData("outside")] [InlineData("read-budget")]
    [InlineData("invocation-budget")] [InlineData("unknown-operation")] [InlineData("result-conflict")]
    [InlineData("duplicate-usage")] [InlineData("positive-cost")]
    public async Task DispatcherStopsExpansionAndNonconvergentLoops(string mode)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = await Fixture.CreateAsync();
        var query = f.Package.Inputs[0].Reference.Retrieve;
        if (mode == "outside") query = query with { Id = "outside-package" };
        if (mode == "read-budget")
        {
            var p = f.Package with { Spec = f.Package.Spec with { Budget = f.Package.Spec.Budget with { MaximumAdditionalReads = 0 } } };
            p = p with { SpecSha256 = EpisodeExecutionValidation.Hash(p.Spec) };
            f.Start = f.Start with { Handoff = HandoffJson.Seal(p) };
            f.Authority.Current = f.Authority.Current with { PackageSha256 = f.Start.Handoff.PackageSha256 };
        }
        if (mode == "invocation-budget") f.Authority.Current = f.Authority.Current with { MaximumInvocations = 1 };
        var read = EpisodeExecutionValidation.Serialize(new EpisodeFrame(1, "retrieve_context", Retrieval: query));
        var usage = EpisodeExecutionValidation.Serialize(new EpisodeFrame(1, "usage", Usage: new("session", "scripted", 12, 5, mode == "positive-cost" ? 1 : 0)));
        f.Runner.Lines = mode switch
        {
            "unknown-operation" => ["{\"schema_version\":1,\"operation\":\"approve\",\"usage\":{}}"],
            "result-conflict" => [f.Submit("same-key", "partial"), f.Submit("same-key", "reported_complete")],
            "duplicate-usage" => [usage, usage], "positive-cost" => [usage], _ => [read]
        };
        var result = await f.RunAsync();
        Assert.Equal("blocked", result.ExecutionOutcome);
        Assert.True(f.Runner.Calls <= 2);
        Assert.Equal(mode == "result-conflict" ? 1 : 0, result.Records.Count(r => r.Kind == "submission"));
        Assert.True(result.Records.Count(r => r.Kind == "usage") <= 1);
    }

    [Fact]
    public async Task LostSubmissionAcknowledgmentIsUnknownAndRetryPreservesTheCommittedReceipt()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = await Fixture.CreateAsync();
        f.UseStore(new LostAcknowledgmentStore(new FileEpisodeStore(Path.Combine(f.Root, "store"))));
        f.Runner.Lines = [f.Submit("checkpoint", "partial")];
        var failed = await f.RunAsync();
        var original = EpisodeExecutionValidation.Data<EpisodeStoredSubmission>(Assert.Single(failed.Records.Where(r => r.Kind == "submission")));
        Assert.Contains(failed.Records, r => r.Kind == "tool" && r.Data.GetProperty("status").GetString() == "outcome_unknown");
        f.Runner.Lines = [f.Submit("checkpoint", "partial"), f.Submit("final", "reported_complete")];
        var resumed = await f.RunAsync(true);
        Assert.Equal("succeeded", resumed.ExecutionOutcome);
        Assert.Equal(2, resumed.Records.Count(r => r.Kind == "submission"));
        Assert.Equal(original.Receipt, EpisodeExecutionValidation.Data<EpisodeStoredSubmission>(resumed.Records.First(r => r.Kind == "submission")).Receipt);
    }

    [Fact]
    public async Task MaterialAuthoredStopDoesNotDispatchEvenAfterAnAuthorizedRead()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = await Fixture.CreateAsync();
        f.Runner.Lines = [f.Submit("decision-needed", "blocked"), EpisodeExecutionValidation.Serialize(
            new EpisodeFrame(1, "retrieve_context", Retrieval: f.Package.Inputs[0].Reference.Retrieve))];
        var result = await f.RunAsync();
        Assert.Equal("blocked", result.ExecutionOutcome); Assert.Equal("succeeded", result.ProcessOutcome);
        Assert.Equal("blocked", result.AuthoredStatus); Assert.Equal(1, f.Runner.Calls);
    }

    [Fact]
    public async Task ActualUsageIsAttributedOnceWhileMissingFieldsStayNull()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = await Fixture.CreateAsync();
        f.Runner.Lines = [EpisodeExecutionValidation.Serialize(new EpisodeFrame(1, "usage",
            Usage: new("script-session", "script-model", 12, null, null))), f.Submit("final", "reported_complete")];
        var result = await f.RunAsync();
        var usage = Assert.Single(result.Records.Where(r => r.Kind == "usage")).Data.GetProperty("usage");
        Assert.Equal(12, usage.GetProperty("input_tokens").GetInt32());
        Assert.Equal(JsonValueKind.Null, usage.GetProperty("cost_usd").ValueKind);
        Assert.DoesNotContain(result.Records, r => r.Kind == "usage_unavailable");
        Assert.Equal(result.Records.Count, (await f.RunAsync()).Records.Count);
    }

    [Fact]
    public async Task InventoryChangeBlocksDeliveryAndReportsTheActualProcessExit()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = await Fixture.CreateAsync();
        f.Runner.Lines = [f.Submit("final", "reported_complete")];
        f.Runner.OnProcess = invocation => File.WriteAllText(Path.Combine(invocation.Environment["HOME"], "unexpected"), "changed by fixture");
        var result = await f.RunAsync();
        Assert.Equal("blocked", result.ExecutionOutcome); Assert.Equal("succeeded", result.ProcessOutcome);
        Assert.Single(result.Records.Single(r => r.Kind == "inventory").Data.GetProperty("changes").EnumerateArray());
    }

    [Fact]
    public async Task RetryBudgetCannotResetAndChangedPackageCannotReuseExecutionKey()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = await Fixture.CreateAsync();
        f.Authority.Current = f.Authority.Current with { MaximumAttempts = 1 };
        f.Runner.Ending = "failed";
        await f.RunAsync();
        Assert.Equal("blocked", (await f.RunAsync(true)).ExecutionOutcome); Assert.Equal(1, f.Runner.Calls);
        var altered = f.Package with { OmissionPolicy = "changed" };
        f.Start = f.Start with { Handoff = HandoffJson.Seal(altered) };
        await Assert.ThrowsAsync<ArgumentException>(() => f.RunAsync(true));
    }

    [Fact]
    public async Task PendingWriteIsDiscoverableAndCorruptCommittedRecordFailsClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), "episode-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileEpisodeStore(root); var id = new string('a', 64);
            await using (await store.AcquireAsync(id, default)) await store.AppendAsync(id, "sample", new { text = "exact 😀" }, default);
            var directory = Path.Combine(root, "episodes-v1", id);
            await File.WriteAllTextAsync(Path.Combine(directory, "partial.pending"), "truncated");
            Assert.Single(await store.ReadAsync(id, default)); Assert.Equal(1, await store.CountUncommittedAsync(id, default));
            var path = Path.Combine(directory, "000001.json");
            await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("exact", "wrong"));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync(id, default));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("{\"schema_version\":1,\"schema_version\":1}")]
    [InlineData("{\"schema_version\":1,\"principal\":\"operator\"}")]
    public void ProviderCannotInjectHostIdentityOrDuplicateFields(string json) =>
        Assert.Throws<ArgumentException>(() => HandoffJson.ParseDocument<EpisodeFrame>(json));

    private sealed class Fixture : IDisposable
    {
        public FindingsFixture Ledger { get; } = new();
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "episode-tests-" + Guid.NewGuid().ToString("N"));
        public EpisodeStart Start { get; set; } = null!;
        public HandoffPackage Package => JsonSerializer.Deserialize<HandoffPackage>(Start.Handoff.PackageJson, HandoffJson.Options)!;
        public AuthoritySource Authority { get; } = new();
        public ScriptedRunner Runner { get; } = new();
        public EpisodeExecutor Executor { get; private set; } = null!;
        public string[] Before { get; private set; } = [];
        public string Id => EpisodeExecutionValidation.ExecutionId(Authority.Current, Start.RequestId);
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture(); Directory.CreateDirectory(f.Root);
            await f.Ledger.OpenAsync();
            var binding = InspectionTests.Binding(f.Ledger); var preparer = new HandoffPreparer(f.Ledger.Service());
            var index = await preparer.IndexAsync(binding, f.Ledger.Root, "task", null, default);
            var handoff = await preparer.PrepareAsync(binding, HandoffTests.Request(index), default);
            f.Start = new(1, "execute-1", handoff);
            var executable = Path.Combine(f.Root, "scripted-provider");
            await File.WriteAllTextAsync(executable, "#!/bin/sh\nexit 0\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            f.Authority.Current = new(1, "trial-author", true, DateTimeOffset.UtcNow.AddHours(1), handoff.PackageSha256,
                f.Ledger.Root, binding, f.Package.Spec.RequestedGrants, true, executable,
                await EpisodeSandbox.HashFileAsync(executable, default), 4, 3, 60, []);
            f.Executor = new(new FileEpisodeStore(Path.Combine(f.Root, "store")), f.Authority, f.Ledger.Service(), f.Runner,
                Path.Combine(f.Root, "scratch"), "test-build");
            f.Before = await InspectionTests.Files(f.Ledger.Directory);
            return f;
        }
        public void UseStore(IEpisodeStore store) => Executor = new(store, Authority, Ledger.Service(), Runner,
            Path.Combine(Root, "scratch"), "test-build");
        public Task<EpisodeInspection> RunAsync(bool resume = false) => Executor.ExecuteAsync(Start, resume, default);
        public string Submit(string key, string status) => EpisodeExecutionValidation.Serialize(new EpisodeFrame(1, "submit_result",
            new(1, key, new(1, Package.Spec.Id, Start.Handoff.PackageSha256, status, "Cited mechanical output; no client judgment claim.", [],
                Package.Spec.AcceptanceChecks.Select(c => new EpisodeCheckResult(c, "not_checked", "Fixture", "Scripted output only")).ToArray(),
                ["Unknown real-task judgment"], [], null, null))));
        public void Dispose() { Ledger.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class AuthoritySource : IEpisodeAuthoritySource
    {
        public EpisodeAuthority Current { get; set; } = null!;
        public Task<EpisodeAuthority> ReadAsync(CancellationToken token) => Task.FromResult(Current);
    }
    private sealed class LostAcknowledgmentStore(IEpisodeStore inner) : IEpisodeStore
    {
        private bool _lost;
        public Task<IAsyncDisposable> AcquireAsync(string id, CancellationToken token) => inner.AcquireAsync(id, token);
        public Task<IReadOnlyList<EpisodeRecord>> ReadAsync(string id, CancellationToken token) => inner.ReadAsync(id, token);
        public Task<int> CountUncommittedAsync(string id, CancellationToken token) => inner.CountUncommittedAsync(id, token);
        public async Task<EpisodeRecord> AppendAsync(string id, string kind, object data, CancellationToken token)
        {
            var record = await inner.AppendAsync(id, kind, data, token);
            if (kind == "submission" && !_lost) { _lost = true; throw new IOException("Simulated lost durable acknowledgement"); }
            return record;
        }
    }
    private sealed class SimulatedHostDeath : Exception;
    private sealed class ScriptedRunner : IProcessRunner
    {
        public string[] Lines { get; set; } = [];
        public string Ending { get; set; } = "success";
        public int Calls { get; private set; }
        public string LastInput { get; private set; } = "";
        public Action? BeforeOutput { get; set; }
        public Action<ProcessInvocation>? OnProcess { get; set; }
        public Action<int>? OnInvocation { get; set; }
        public async Task<ProcessExit> RunAsync(ProcessInvocation invocation, Func<string, CancellationToken, ValueTask> stdout,
            Func<string, CancellationToken, ValueTask> stderr, TruncatedLineTally? tally, CancellationToken token)
        {
            Calls++; OnProcess?.Invoke(invocation); LastInput = invocation.StandardInput; OnInvocation?.Invoke(Calls); BeforeOutput?.Invoke();
            foreach (var line in Lines) await stdout(line, token);
            if (Ending == "cancelled") throw new OperationCanceledException();
            if (Ending == "blocked") throw new ProviderProcessTimeoutException(invocation.Timeout);
            if (Ending == "crash") throw new SimulatedHostDeath();
            return new(Ending == "failed" ? 7 : 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0);
        }
    }
}
