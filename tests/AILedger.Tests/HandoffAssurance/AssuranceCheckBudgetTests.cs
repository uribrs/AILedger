using System.Text;
using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Assurance;
using AILedger.Core.Episodes;
using AILedger.Core.Handoffs;
using AILedger.Providers.Assurance;
using AILedger.Providers.Process;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Tests.HandoffAssurance;

public sealed class AssuranceCheckBudgetTests
{
    [Fact]
    public async Task BuiltCliRequiresConfirmationAndReplaysTheSameGrantAcrossProcesses()
    {
        using var f = await AssuranceFixture.CreateAsync();
        await ConsumeAsync(f, 8);
        var request = await RequestAsync(f);
        var authority = Path.Combine(f.Root, "policy.json");
        await File.WriteAllTextAsync(authority, JsonSerializer.Serialize(f.Policy, HandoffJson.Options));
        var denied = await RunCliAsync(false);
        Assert.Equal(2, denied.Exit); Assert.Contains("confirm", denied.Error);
        Assert.Empty(await GrantsAsync(f));
        var first = await RunCliAsync(true);
        Assert.True(first.Exit == 0, first.Error);
        var second = await RunCliAsync(true);
        Assert.True(second.Exit == 0, second.Error);
        var granted = JsonSerializer.Deserialize<AssuranceCheckBudgetExtension>(first.Output, HandoffJson.Options)!;
        var replayed = JsonSerializer.Deserialize<AssuranceCheckBudgetExtension>(second.Output, HandoffJson.Options)!;
        Assert.False(granted.Replayed); Assert.True(replayed.Replayed);
        Assert.Equal(granted.Grant, replayed.Grant);
        Assert.Single(await GrantsAsync(f));

        async Task<(int Exit, string Output, string Error)> RunCliAsync(bool confirm)
        {
            var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var dotnet = Path.GetFullPath(Path.Combine(runtime, "..", "..", "..", "dotnet"));
            var assembly = typeof(AssuranceCheckBudgetTests).Assembly.Location;
            var args = new List<string> { "exec", "--runtimeconfig", Path.ChangeExtension(assembly, "runtimeconfig.json"),
                "--depsfile", Path.ChangeExtension(assembly, "deps.json"), typeof(CliApplication).Assembly.Location,
                "assurance", "extend-check-budget", "--authority", authority, "--store", Path.Combine(f.Root, "store"),
                "--principal", "operator", "--session", "cli-renewal", "--body-stdin" };
            if (confirm) args.Add("--confirm-budget-extension");
            var output = new StringBuilder(); var error = new StringBuilder();
            var result = await new SystemProcessRunner().RunAsync(new(dotnet, f.Root, args,
                JsonSerializer.Serialize(request, HandoffJson.Options), new Dictionary<string, string>(), TimeSpan.FromSeconds(30)),
                (line, _) => { output.AppendLine(line); return ValueTask.CompletedTask; },
                (line, _) => { error.AppendLine(line); return ValueTask.CompletedTask; }, null, default);
            return (result.ExitCode, output.ToString(), error.ToString());
        }
    }

    [Fact]
    public async Task ExhaustedCaseContinuesWithoutErasingFindingsOrGrantingAcceptance()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var finding = await f.ReportAsync("reviewer", checkStatus: "fail",
            findings: [new("configuration", "defect", "Configured check misses required coverage", ["a.cs"], [])]);
        await ConsumeAsync(f, 8);
        Assert.Equal("capacity_exceeded", (await CheckAsync(f, "ninth")).Error?.Code);
        var request = await RequestAsync(f, 2);
        var extension = await f.Sessions["operator"].ExtendCheckBudgetAsync(request, true, default);

        f.OpenSessions();
        var replay = await f.Sessions["operator"].ExtendCheckBudgetAsync(request, true, default);
        Assert.True(replay.Replayed);
        Assert.Equal(extension.Grant, replay.Grant);
        Assert.Null((await CheckAsync(f, "ninth")).Error);
        Assert.Null((await CheckAsync(f, "tenth")).Error);
        Assert.Equal("capacity_exceeded", (await CheckAsync(f, "eleventh")).Error?.Code);
        var inspected = await f.InspectAsync();
        Assert.Equal(10, inspected.CheckBudget!.ConsumedBatches);
        Assert.Equal(0, inspected.CheckBudget.RemainingBatches);
        Assert.Contains(finding.Response.Receipt!.Id + ":configuration", inspected.UnresolvedFindings);
        Assert.Equal("not_accepted", inspected.Acceptance);
        Assert.Single(await GrantsAsync(f));
    }

    [Theory]
    [InlineData("reviewer")]
    [InlineData("verifier")]
    [InlineData("synthesizer")]
    public async Task InspectorsCannotRenewOrReachRenewalThroughTheToolSurface(string principal)
    {
        using var f = await AssuranceFixture.CreateAsync();
        var request = await RequestAsync(f);
        var error = await Assert.ThrowsAsync<AssuranceRefusal>(() =>
            f.Sessions[principal].ExtendCheckBudgetAsync(request, true, default));
        Assert.Equal("authorization_denied", error.Code);
        Assert.Equal("authorization_denied", (await f.CallAsync("operator", "extend_check_budget", request)).Error?.Code);
        Assert.Empty(await GrantsAsync(f));
    }

    [Theory]
    [InlineData("implementer", "self_approval")]
    [InlineData("partial-scope", "authorization_denied")]
    [InlineData("disabled", "authorization_denied")]
    [InlineData("expired", "authorization_denied")]
    public async Task RenewalRequiresCurrentIndependentCaseWideAuthority(string change, string code)
    {
        using var f = await AssuranceFixture.CreateAsync();
        f.Policy = f.Policy with
        {
            Implementer = change == "implementer" ? "operator" : f.Policy.Implementer,
            Principals = f.Policy.Principals.Select(p => p.Id != "operator" ? p : p with
            {
                Areas = change == "partial-scope" ? ["a"] : p.Areas,
                Enabled = change != "disabled",
                ExpiresAt = change == "expired" ? DateTimeOffset.UtcNow.AddMinutes(-1) : p.ExpiresAt
            }).ToArray()
        };
        f.OpenSessions();
        var request = new ExtendAssuranceCheckBudgetRequest(1, "renew", PolicyIdentity(f.Policy), 8, 1, "Repair host checks");
        var error = await Assert.ThrowsAsync<AssuranceRefusal>(() =>
            f.Sessions["operator"].ExtendCheckBudgetAsync(request, true, default));
        Assert.Equal(code, error.Code);
        Assert.Empty(await GrantsAsync(f));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-1)]
    public async Task ExtensionIsBounded(int additional)
    {
        using var f = await AssuranceFixture.CreateAsync();
        var request = await RequestAsync(f, additional);
        Assert.Equal("invalid_request", (await Assert.ThrowsAsync<AssuranceRefusal>(() =>
            f.Sessions["operator"].ExtendCheckBudgetAsync(request, true, default))).Code);
        Assert.Empty(await GrantsAsync(f));
    }

    [Fact]
    public async Task ConfirmationReasonFreshCountAndExhaustionAreRequired()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var request = await RequestAsync(f);
        var service = f.Sessions["operator"];
        Assert.Equal("confirmation_required", (await Assert.ThrowsAsync<AssuranceRefusal>(() =>
            service.ExtendCheckBudgetAsync(request, false, default))).Code);
        Assert.Equal("invalid_request", (await Assert.ThrowsAsync<AssuranceRefusal>(() =>
            service.ExtendCheckBudgetAsync(request with { Reason = " " }, true, default))).Code);
        Assert.Equal("budget_not_exhausted", (await Assert.ThrowsAsync<AssuranceRefusal>(() =>
            service.ExtendCheckBudgetAsync(request, true, default))).Code);
        await ConsumeAsync(f, 8);
        Assert.Equal("stale_snapshot", (await Assert.ThrowsAsync<AssuranceRefusal>(() =>
            service.ExtendCheckBudgetAsync(request, true, default))).Code);
        Assert.Equal("authority_changed", (await Assert.ThrowsAsync<AssuranceRefusal>(() =>
            service.ExtendCheckBudgetAsync(request with { ExpectedPolicySha256 = new string('0', 64) }, true, default))).Code);
        Assert.Empty(await GrantsAsync(f));
    }

    [Fact]
    public async Task PolicyCorrectionsRequireFreshGrantAndOldKeysCannotBeRebound()
    {
        using var f = await AssuranceFixture.CreateAsync();
        f.Runner.Outcome = "failure";
        await ConsumeAsync(f, 8);
        var request = await RequestAsync(f, 2);
        await f.Sessions["operator"].ExtendCheckBudgetAsync(request, true, default);
        f.Policy = f.Policy with { Checks = f.Policy.Checks.Select(c => c with { Arguments = ["correct-filter"] }).ToArray() };
        f.OpenSessions();
        Assert.Equal("capacity_exceeded", (await CheckAsync(f, "corrected")).Error?.Code);
        Assert.Equal("request_conflict", (await Assert.ThrowsAsync<AssuranceRefusal>(() =>
            f.Sessions["operator"].ExtendCheckBudgetAsync(request, true, default))).Code);
        var corrected = (await RequestAsync(f)) with { RequestId = "corrected-policy-renewal" };
        await f.Sessions["operator"].ExtendCheckBudgetAsync(corrected, true, default);
        f.Runner.Outcome = "success";
        Assert.Null((await CheckAsync(f, "corrected")).Error);
        Assert.Equal(2, (await GrantsAsync(f)).Count);
        Assert.Equal(9, (await f.InspectAsync()).CheckBudget!.ConsumedBatches);
    }

    [Fact]
    public async Task UnknownEndingMustBeReconciledAndStillConsumesItsBatch()
    {
        using var f = await AssuranceFixture.CreateAsync();
        await ConsumeAsync(f, 7);
        f.Runner.Outcome = "crash";
        await Assert.ThrowsAsync<HostDeath>(() => CheckAsync(f, "interrupted"));
        var request = await RequestAsync(f);
        Assert.Equal("inspection_interrupted", (await Assert.ThrowsAsync<AssuranceRefusal>(() =>
            f.Sessions["operator"].ExtendCheckBudgetAsync(request, true, default))).Code);
        await f.Sessions["operator"].ReconcileChecksAsync("interrupted", "verifier", true, default);
        await f.Sessions["operator"].ExtendCheckBudgetAsync(request, true, default);
        f.Runner.Outcome = "success";
        Assert.Equal("inspection_interrupted", (await CheckAsync(f, "interrupted")).Error?.Code);
        Assert.Null((await CheckAsync(f, "after-reconciliation")).Error);
        Assert.Equal(9, f.Runner.Calls);
    }

    [Fact]
    public async Task ConcurrentRenewalsCannotStackAndLostAcknowledgmentReplaysOnce()
    {
        using var f = await AssuranceFixture.CreateAsync();
        await ConsumeAsync(f, 8);
        var request = await RequestAsync(f);
        var service = f.Sessions["operator"];
        var results = await Task.WhenAll(new[] { "renew-one", "renew-two" }.Select(key =>
            Record.ExceptionAsync(() => service.ExtendCheckBudgetAsync(request with { RequestId = key }, true, default))));
        Assert.Single(results.Where(e => e is null));
        Assert.Equal("budget_not_exhausted", Assert.IsType<AssuranceRefusal>(Assert.Single(results.Where(e => e is not null))).Code);
        Assert.Single(await GrantsAsync(f));
        await CheckAsync(f, "consume-extension");
        f.Store = new LostGrantResponseStore(f.Store); f.OpenSessions();
        request = (await RequestAsync(f)) with { RequestId = "lost-acknowledgment" };
        await Assert.ThrowsAsync<IOException>(() => f.Sessions["operator"].ExtendCheckBudgetAsync(request, true, default));
        f.OpenSessions();
        Assert.True((await f.Sessions["operator"].ExtendCheckBudgetAsync(request, true, default)).Replayed);
        Assert.Equal(2, (await GrantsAsync(f)).Count);
        Assert.Equal(1, (await f.InspectAsync()).CheckBudget!.RemainingBatches);
    }

    private static async Task ConsumeAsync(AssuranceFixture f, int count)
    {
        for (var i = 0; i < count; i++) Assert.Null((await CheckAsync(f, "initial-" + i)).Error);
    }

    private static async Task<AssuranceResponse> CheckAsync(AssuranceFixture f, string key)
    {
        var snapshot = (await f.InspectAsync("verifier")).Snapshot;
        return await f.CallAsync("verifier", "run_assurance_checks",
            new RunAssuranceChecksRequest(1, key, "a", snapshot.BindingSha256, ["unit"]));
    }

    private static async Task<ExtendAssuranceCheckBudgetRequest> RequestAsync(AssuranceFixture f, int additional = 1)
    {
        var budget = (await f.InspectAsync()).CheckBudget!;
        return new(1, "renew", budget.PolicySha256, budget.ConsumedBatches, additional, "Correct the host check configuration and reverify");
    }

    private static async Task<IReadOnlyList<EpisodeRecord>> GrantsAsync(AssuranceFixture f) =>
        (await f.Store.ReadAsync(Hash(new { Namespace = "assurance-v1", f.Policy.CaseId }), default))
            .Where(r => r.Kind == "assurance_check_budget_extended").ToArray();

    private sealed class LostGrantResponseStore(IEpisodeStore inner) : IEpisodeStore
    {
        private bool _lost;
        public Task<IAsyncDisposable> AcquireAsync(string id, CancellationToken token) => inner.AcquireAsync(id, token);
        public Task<IReadOnlyList<EpisodeRecord>> ReadAsync(string id, CancellationToken token) => inner.ReadAsync(id, token);
        public Task<int> CountUncommittedAsync(string id, CancellationToken token) => inner.CountUncommittedAsync(id, token);
        public async Task<EpisodeRecord> AppendAsync(string id, string kind, object data, CancellationToken token)
        {
            var record = await inner.AppendAsync(id, kind, data, token);
            if (!_lost && kind == "assurance_check_budget_extended") { _lost = true; throw new IOException("Lost grant acknowledgment"); }
            return record;
        }
    }
}
