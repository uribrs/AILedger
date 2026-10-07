using AILedger.Core.Application;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Providers.Assurance;
using AILedger.Storage;
using AILedger.Storage.Episodes;
using AILedger.Tests.Support;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Tests.Orchestration;

[Collection(StandardInput.Collection)]
public sealed class AssuranceOwnershipTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CheckHoldsTaskLockButLeaseExpiryFencesItsReceiptAndRetainsUnknownAttempt(bool expire)
    {
        using var f = await CompletionFixture.OpenAsync(); await f.RunAsync();
        var clock = new TestClock();
        var host = new FileGovernedTaskService(f.Driver.Bundle.Root, new CommandHandler(), new TaskReducer(), coordinationTimeProvider: clock);
        var lease = await host.AcquireCoordinationAsync(new("T1"), "check-owner", TimeSpan.FromSeconds(120), default);
        var bound = host.BindCoordination(lease);
        var state = (await bound.GetStateAsync(new("T1"), default))!;
        var session = new AssuranceSession("verifier", "controlled-check", "external-client", null);
        var runner = new ControlledRunner();
        var service = new AssuranceService(new FileEpisodeStore(f.Driver.Options.Dispatch.AssuranceStore!), new PolicySource(f.Policy), f.Policy,
            session, runner, governedContext: bound.CreateAssuranceContext(state, session, false, DateTimeOffset.UtcNow.AddHours(1), f.Driver.Options.Dispatch.AssuranceStore));
        var snapshot = (await RepairContinuationTests.InspectAsync(service, "a")).Snapshot;
        var request = new RunAssuranceChecksRequest(1, "controlled-check", "a", snapshot.BindingSha256, ["unit"]);
        var check = service.InvokeAsync("run_assurance_checks", Json(request), default);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var heartbeat = host.RenewCoordinationAsync(lease, TimeSpan.FromSeconds(120), default);
        Assert.False(heartbeat.IsCompleted); // Deterministic runner barrier holds the task lock.
        clock.Advance(TimeSpan.FromSeconds(expire ? 121 : 40));
        runner.Finish.SetResult();
        var response = await check;
        Assert.Equal(1, runner.Calls);
        if (expire)
        {
            Assert.Equal("governed_admission", response.Error?.Code); Assert.Null(response.Receipt);
            await Assert.ThrowsAsync<GovernanceException>(() => heartbeat);
            var operatorService = await RepairContinuationTests.OpenAsync(f, "operator", "reconcile-check");
            Assert.Contains("controlled-check", (await RepairContinuationTests.InspectAsync(operatorService, "a")).PendingChecks);
            await operatorService.ReconcileChecksAsync("controlled-check", "verifier", true, default);
            var newOwner = await host.AcquireCoordinationAsync(new("T1"), "new-owner", TimeSpan.FromSeconds(120), default);
            var newHost = host.BindCoordination(newOwner);
            var recovered = new AssuranceService(new FileEpisodeStore(f.Driver.Options.Dispatch.AssuranceStore!), new PolicySource(f.Policy), f.Policy,
                session, runner, governedContext: newHost.CreateAssuranceContext(state, session, false, DateTimeOffset.UtcNow.AddHours(1), f.Driver.Options.Dispatch.AssuranceStore));
            Assert.Equal("inspection_interrupted", (await recovered.InvokeAsync("run_assurance_checks", Json(request), default)).Error?.Code);
            Assert.Equal(1, runner.Calls);
            Assert.Null((await recovered.InvokeAsync("run_assurance_checks", Json(request with { RequestId = "explicit-new-check" }), default)).Error);
            Assert.Equal(2, runner.Calls);
            await host.ReleaseCoordinationAsync(newOwner, default);
        }
        else
        {
            Assert.Null(response.Error); await heartbeat;
            var replay = await service.InvokeAsync("run_assurance_checks", Json(request), default);
            Assert.True(replay.Replayed); Assert.Equal(1, runner.Calls);
            await host.ReleaseCoordinationAsync(lease, default);
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingOrFailedRequiredChecksStopBeforeBlindReview(bool failed)
    {
        using var f = await CompletionFixture.OpenAsync();
        if (failed) await f.SavePolicyAsync(f.Policy with { Checks = f.Policy.Checks.Select(c => c with { Arguments = ["-c", "exit 7"] }).ToArray() });
        f.Driver.Bundle.Adapter.BeforeResult = async request =>
        {
            using var relay = await CognitiveRelay.OpenAsync(request.FindingsEndpoint!);
            if (failed)
            {
                var inspected = await relay.CallAsync("inspect_assurance", Json(new InspectAssuranceRequest(1, "a")));
                var binding = inspected.GetProperty("data").GetProperty("snapshot").GetProperty("binding_sha256").GetString()!;
                var check = await relay.CallAsync("run_assurance_checks", Json(new RunAssuranceChecksRequest(1, "diagnostic-failure", "a", binding, ["unit"])));
                Assert.Equal("recorded", check.GetProperty("status").GetString());
            }
            await relay.AssessAsync(CognitiveWorkKind.Verification); await relay.FinishAsync();
        };
        var result = await f.RunAsync();
        Assert.Equal("required_checks_pending", result.Code);
        Assert.Single(result.Dispatches);
        Assert.DoesNotContain((await f.Driver.Bundle.State()).Runs.Values, r => r.SubjectRole == RoleKind.CodeReviewer);
        await f.Driver.Bundle.Ok("stage", "transition", "--stage", "review");
        var resumed = await f.RunAsync();
        Assert.Equal("required_checks_pending", resumed.Code);
        Assert.Empty(resumed.Dispatches);
        Assert.DoesNotContain((await f.Driver.Bundle.State()).Runs.Values, r => r.SubjectRole == RoleKind.CodeReviewer);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NewCaseOrStoreCannotEraseGovernedFindingsAndBudget(bool changeStore)
    {
        using var f = await CompletionFixture.OpenAsync(); await f.RunAsync();
        Assert.NotNull((await f.AcceptAsync(withFinding: true)).Error);
        var store = f.Driver.Options.Dispatch.AssuranceStore!;
        if (changeStore) store += "-new";
        else await f.SavePolicyAsync(f.Policy with { CaseId = "replacement-case" });
        var error = await Assert.ThrowsAsync<AssuranceRefusal>(() => AILedger.Cli.Assurance.AssuranceHost.OpenAsync(
            f.Driver.Authority, store, new("operator", "new-case-attempt", "external-client", null), null, default,
            (FileGovernedTaskService)f.Driver.Bundle.Service));
        Assert.Equal("assurance_case_changed", error.Code);
        Assert.NotEqual(WorkItemStatus.Completed, (await f.Driver.Bundle.State()).WorkItems[new("A")].Status);
    }

    private sealed class PolicySource(AssurancePolicy policy) : IAssurancePolicySource
    { public Task<AssurancePolicy> ReadAsync(CancellationToken token) => Task.FromResult(policy); }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan amount) => _now += amount;
    }
    private sealed class ControlledRunner : IProcessRunner
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        public async Task<ProcessExit> RunAsync(ProcessInvocation invocation, Func<string, CancellationToken, ValueTask> stdout,
            Func<string, CancellationToken, ValueTask> stderr, TruncatedLineTally? tally, CancellationToken token)
        {
            Calls++; var started = DateTimeOffset.UtcNow; Started.TrySetResult();
            await Finish.Task.WaitAsync(token); await stdout("Controlled check observed return one", token);
            return new(0, started, DateTimeOffset.UtcNow, 0);
        }
    }
}
