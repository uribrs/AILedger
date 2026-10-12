using AILedger.Core.Assurance;
using AILedger.Core.Episodes;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Tests.HandoffAssurance;

public sealed class AssurancePlanningTests
{
    [Fact]
    public async Task AreaScopedInspectorDoesNotReceiveCaseWidePlanningProse()
    {
        using var f = await AssuranceFixture.CreateAsync();
        f.Policy = f.Policy with
        {
            Plan = new("Includes rationale about areas outside the reviewer's grant.", 12, "Case-wide repair allowance."),
            Principals = f.Policy.Principals.Select(p => p.Id == "reviewer" ? p with { Areas = ["a"] } : p).ToArray()
        };
        f.OpenSessions();
        Assert.Null((await f.InspectAsync("reviewer")).Plan);
        Assert.Equal(f.Policy.Plan, (await f.InspectAsync("operator")).Plan);
    }

    [Fact]
    public async Task LostPlanAcknowledgmentStopsBeforeChecksAndExactRetryRecordsThePlanOnce()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var snapshot = (await f.InspectAsync("verifier")).Snapshot;
        f.Policy = f.Policy with { Plan = new("Three changed areas.", 12, "Initial pass and repair allowance.") };
        f.Store = new LostPlanResponseStore(f.Store);
        f.OpenSessions();
        var request = new RunAssuranceChecksRequest(1, "stable-check", "a", snapshot.BindingSha256, ["unit"]);
        Assert.Equal("outcome_unknown", (await f.CallAsync("verifier", "run_assurance_checks", request)).Error?.Code);
        Assert.Equal(0, f.Runner.Calls);
        Assert.Null((await f.CallAsync("verifier", "run_assurance_checks", request)).Error);
        Assert.True((await f.CallAsync("verifier", "run_assurance_checks", request)).Replayed);
        Assert.Equal(1, f.Runner.Calls);
        var records = await f.Store.ReadAsync(Hash(new { Namespace = "assurance-v1", f.Policy.CaseId }), default);
        Assert.Single(records.Where(r => r.Kind == "assurance_plan_recorded"));
        Assert.Single(records.Where(r => r.Kind == "assurance_check_started"));
    }

    [Fact]
    public async Task ApprovedScopeCanExceedEightAndThirtyTwoAreasWithRecordedPlanAndDependencies()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var checks = Enumerable.Range(0, 40).Select(i => f.Policy.Checks[0] with { Id = "unit" + i }).ToArray();
        var areas = Enumerable.Range(0, 40).Select(i => f.Policy.Areas[0] with
        {
            Id = "area" + i,
            Criteria = [new("behavior", "Component-specific behavior", checks[i].Id)],
            DependsOnAreas = i == 39 ? Enumerable.Range(0, 39).Select(j => "area" + j).ToArray() : []
        }).ToArray();
        f.Policy = f.Policy with
        {
            Areas = areas,
            Checks = checks,
            Principals = f.Policy.Principals.Select(p => p with { Areas = areas.Select(a => a.Id).ToArray() }).ToArray(),
            Plan = new("Each changed component has separate assurance; the final consumer relies on all producers.",
                60, "Forty initial area batches plus twenty for repairs and fresh verifier checks.")
        };
        f.OpenSessions();
        var authority = Path.Combine(f.Root, "planned-policy.json");
        await File.WriteAllTextAsync(authority, Json(f.Policy).GetRawText());
        var host = await AILedger.Cli.Assurance.AssuranceHost.OpenAsync(authority, Path.Combine(f.Root, "store"),
            new("operator", "planned-host", "external-client", null), null, default);
        Assert.Null((await host.InvokeAsync("inspect_assurance", Json(new InspectAssuranceRequest(1, "area39")), default)).Error);
        var initial = await f.InspectAsync(area: "area39");
        Assert.Equal(f.Policy.Plan, initial.Plan);
        Assert.Equal(39, initial.Snapshot.Dependencies.Count);
        Assert.Equal(60, initial.CheckBudget!.BatchLimit);
        foreach (var area in areas.Take(9))
        {
            var snapshot = (await f.InspectAsync("verifier", area.Id)).Snapshot;
            var result = await f.CallAsync("verifier", "run_assurance_checks",
                new RunAssuranceChecksRequest(1, area.Id, area.Id, snapshot.BindingSha256, [area.Criteria[0].CheckId]));
            Assert.Null(result.Error);
        }
        var after = await f.InspectAsync(area: "area39");
        Assert.Equal(9, after.CheckBudget!.ConsumedBatches);
        Assert.Equal(51, after.CheckBudget.RemainingBatches);
        Assert.Equal("not_accepted", after.Acceptance);
        var records = await f.Store.ReadAsync(Hash(new { Namespace = "assurance-v1", f.Policy.CaseId }), default);
        var plan = Assert.Single(records.Where(r => r.Kind == "assurance_plan_recorded"));
        Assert.Equal(40, plan.Data.GetProperty("areas").GetArrayLength());
        Assert.Equal(40, plan.Data.GetProperty("checks").GetArrayLength());
        Assert.Equal(PolicyIdentity(f.Policy), plan.Data.GetProperty("policy_sha256").GetString());
        Assert.Equal("assurance_plan_recorded", records[0].Kind);
    }

    [Theory]
    [InlineData("area-rationale")]
    [InlineData("budget-rationale")]
    [InlineData("insufficient-budget")]
    [InlineData("missing-plan")]
    public async Task UnsupportedPlanningJudgmentIsRefusedBeforeExecution(string invalid)
    {
        using var f = await AssuranceFixture.CreateAsync();
        f.Policy = f.Policy with
        {
            Plan = new(invalid == "area-rationale" ? " " : "Three independent changes.",
                invalid == "insufficient-budget" ? 2 : 12,
                invalid == "budget-rationale" ? " " : "Initial verification with repair allowance.")
        };
        if (invalid == "missing-plan")
            f.Policy = f.Policy with { Plan = null, Areas = Enumerable.Range(0, 9).Select(i => f.Policy.Areas[0] with { Id = "a" + i }).ToArray() };
        Assert.Throws<AssuranceRefusal>(() => Policy(f.Policy));
        Assert.Equal(0, f.Runner.Calls);
    }

    [Theory]
    [InlineData(5, 2)]
    [InlineData(300, 9)]
    public async Task CoverageLowerBoundAccountsForBatchSizeAndConfiguredTimeouts(int timeout, int expected)
    {
        using var f = await AssuranceFixture.CreateAsync();
        var checks = Enumerable.Range(0, 9).Select(i => f.Policy.Checks[0] with { Id = "check" + i, TimeoutSeconds = timeout }).ToArray();
        f.Policy = f.Policy with
        {
            Areas = [f.Policy.Areas[0] with { Criteria = checks.Select(c => new AssuranceCriterion(c.Id, "Required behavior", c.Id)).ToArray() }],
            Checks = checks,
            Principals = f.Policy.Principals.Select(p => p with { Areas = ["a"] }).ToArray(),
            Plan = new("One changed component.", expected - 1, "One complete pass.")
        };
        Assert.Equal(expected, AssurancePlanning.MinimumCheckBatches(f.Policy));
        Assert.Throws<AssuranceRefusal>(() => Policy(f.Policy));
        Policy(f.Policy with { Plan = f.Policy.Plan with { CheckBatchLimit = expected } });
    }

    [Fact]
    public async Task LegacyIdentitiesAndAllowanceRemainExactAndMigrationKeepsConsumption()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var oldPolicyShape = new { f.Policy.SchemaVersion, f.Policy.CaseId, f.Policy.CandidateRoot,
            f.Policy.Implementer, f.Policy.Areas, f.Policy.Principals, f.Policy.Checks };
        Assert.Equal(Hash(oldPolicyShape), PolicyIdentity(f.Policy));
        var oldRequest = new { SchemaVersion = 1, RequestId = "grant", ExpectedPolicySha256 = PolicyIdentity(f.Policy),
            ExpectedConsumedBatches = 8, AdditionalBatches = 1, Reason = "Repair" };
        Assert.Equal(Hash(oldRequest), Hash(new ExtendAssuranceCheckBudgetRequest(1, "grant", PolicyIdentity(f.Policy), 8, 1, "Repair")));
        var before = await f.InspectAsync("verifier");
        Assert.Equal(8, before.CheckBudget!.BatchLimit);
        Assert.Null(before.Plan);
        var check = new RunAssuranceChecksRequest(1, "old-check", "a", before.Snapshot.BindingSha256, ["unit"]);
        Assert.Null((await f.CallAsync("verifier", "run_assurance_checks", check)).Error);
        f.Policy = f.Policy with { Plan = new("Three developed areas.", 15, "Initial pass and repair allowance.") };
        Assert.Equal("authority_changed", (await f.CallAsync("verifier", "inspect_assurance", new InspectAssuranceRequest(1, "a"))).Error?.Code);
        f.OpenSessions();
        var after = await f.InspectAsync();
        Assert.Equal(1, after.CheckBudget!.ConsumedBatches);
        Assert.Equal(14, after.CheckBudget.RemainingBatches);
        Assert.Contains(after.History, h => h.Operation == "run_assurance_checks");
        Assert.Equal("request_conflict", (await f.CallAsync("verifier", "run_assurance_checks", check)).Error?.Code);
    }

    private sealed class LostPlanResponseStore(IEpisodeStore inner) : IEpisodeStore
    {
        private bool _lost;
        public Task<IAsyncDisposable> AcquireAsync(string id, CancellationToken token) => inner.AcquireAsync(id, token);
        public Task<IReadOnlyList<EpisodeRecord>> ReadAsync(string id, CancellationToken token) => inner.ReadAsync(id, token);
        public Task<int> CountUncommittedAsync(string id, CancellationToken token) => inner.CountUncommittedAsync(id, token);
        public async Task<EpisodeRecord> AppendAsync(string id, string kind, object data, CancellationToken token)
        {
            var result = await inner.AppendAsync(id, kind, data, token);
            if (!_lost && kind == "assurance_plan_recorded") { _lost = true; throw new IOException("Lost plan acknowledgment"); }
            return result;
        }
    }
}
