using AILedger.Core.Application;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Providers.Assurance;
using AILedger.Storage;
using AILedger.Storage.Episodes;
using AILedger.Tests.Support;
using AILedger.Tests.Cli;
using AILedger.Cli.Tasks;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Tests.Orchestration;

[Collection(StandardInput.Collection)]
public sealed class AssuranceOwnershipTests
{
    [Fact]
    public async Task LongGovernedCheckSurvivesClosureMonitorLockContentionAndRecordsOnce()
    {
        using var f = await CompletionFixture.OpenAsync(); await f.RunAsync();
        var host = (FileGovernedTaskService)f.Driver.Bundle.Service;
        var state = await f.Driver.Bundle.State();
        var session = new AssuranceSession("verifier", "long-check", "external-client", null);
        var runner = new ControlledRunner();
        var service = new AssuranceService(new FileEpisodeStore(f.Driver.Options.Dispatch.AssuranceStore!), new PolicySource(f.Policy), f.Policy,
            session, runner, governedContext: host.CreateAssuranceContext(state, session, false, DateTimeOffset.UtcNow.AddHours(1), f.Driver.Options.Dispatch.AssuranceStore));
        var before = await RepairContinuationTests.InspectAsync(service, "a");
        var request = new RunAssuranceChecksRequest(1, "long-check", "a", before.Snapshot.BindingSha256, ["unit"]);
        await using var monitor = await HumanClosureMonitor.StartAsync(host, state.TaskId, default);
        var check = service.InvokeAsync("run_assurance_checks", Json(request), monitor.Token);
        AssuranceResponse response;
        try
        {
            await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Exercise the real 30-second task-lock deadline, not a simulated provider heartbeat.
            await Task.Delay(TimeSpan.FromSeconds(35));
            Assert.False(monitor.Token.IsCancellationRequested);
        }
        finally
        {
            runner.Finish.TrySetResult();
            response = await check.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Null(response.Error);
        Assert.NotNull(response.Receipt);
        Assert.True((await service.InvokeAsync("run_assurance_checks", Json(request), default)).Replayed);
        Assert.Equal(1, runner.Calls);
        var after = await RepairContinuationTests.InspectAsync(service, "a");
        Assert.Equal(before.CheckBudget!.ConsumedBatches + 1, after.CheckBudget!.ConsumedBatches);
        Assert.Empty(after.PendingChecks);

        await host.ExecuteAsync(state.TaskId, AILedger.Tests.Core.HumanTaskClosureTests.Confirm(await f.Driver.Bundle.State()), default);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = monitor.Token.Register(() => cancelled.TrySetResult());
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task HumanClosureEndsExistingAssuranceAuthorityWithoutManufacturingAcceptance()
    {
        using var f = await CompletionFixture.OpenAsync(); await f.RunAsync();
        var accepting = await RepairContinuationTests.OpenAsync(f, "operator", "human-closure-operator");
        var before = await RepairContinuationTests.InspectAsync(accepting, "a");
        var state = await f.Driver.Bundle.State();
        await f.Driver.Bundle.Service.ExecuteAsync(state.TaskId,
            AILedger.Tests.Core.HumanTaskClosureTests.Confirm(state), default);
        var inspection = await accepting.InvokeAsync("inspect_assurance", Json(new InspectAssuranceRequest(1, "a")), default);
        Assert.Equal("authorization_denied", inspection.Error!.Code);
        Assert.Equal("not_accepted", before.Acceptance);
        Assert.Equal("succeeded_by_human_override", (await f.Driver.Bundle.State()).HumanClosure!.Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BudgetRenewalPreservesGovernedAnchorBindingAndUnresolvedFindings(bool planned)
    {
        using var f = await CompletionFixture.OpenAsync();
        if (planned) await f.SavePolicyAsync(f.Policy with { Plan = new("Separate changed components and their dependencies.", 12,
            "Initial checks plus repair and fresh verifier coverage.") });
        await f.RunAsync();
        Assert.NotNull((await f.AcceptAsync(withFinding: true)).Error);
        var verifier = await RepairContinuationTests.OpenAsync(f, "verifier", "budget-verifier");
        var before = await RepairContinuationTests.InspectAsync(verifier, "a");
        var target = planned ? 11 : 8;
        for (var i = before.CheckBudget!.ConsumedBatches; i < target; i++)
            Assert.Null((await verifier.InvokeAsync("run_assurance_checks",
                Json(new RunAssuranceChecksRequest(1, "budget-" + i, "a", before.Snapshot.BindingSha256, ["unit"])), default)).Error);
        var host = (FileGovernedTaskService)f.Driver.Bundle.Service;
        var anchor = (await host.InspectCoordinationAsync(new("T1"), default))!.AssuranceAnchor;
        var accepting = await RepairContinuationTests.OpenAsync(f, "operator", "budget-operator");
        var budget = (await RepairContinuationTests.InspectAsync(accepting, "a")).CheckBudget!;
        await accepting.ExtendCheckBudgetAsync(new(1, "renew", budget.PolicySha256, target, planned ? 12 : 1,
            "Reverify corrected checks without resetting the governed case")
            { ExpectedBatchLimit = planned ? budget.BatchLimit : null }, true, default);
        var after = await RepairContinuationTests.InspectAsync(accepting, "a");
        Assert.Equal(before.Snapshot.BindingSha256, after.Snapshot.BindingSha256);
        Assert.Contains(f.FindingId!, after.UnresolvedFindings);
        Assert.Equal(Hash(anchor), Hash((await host.InspectCoordinationAsync(new("T1"), default))!.AssuranceAnchor));
        Assert.Equal("not_accepted", after.Acceptance);
        Assert.Equal(planned ? 24 : 9, after.CheckBudget!.BatchLimit);
        Assert.Equal(target, after.CheckBudget.ConsumedBatches);
        Assert.Null((await verifier.InvokeAsync("run_assurance_checks",
            Json(new RunAssuranceChecksRequest(1, "after-renewal", "a", after.Snapshot.BindingSha256, ["unit"])), default)).Error);
        Assert.NotEqual(WorkItemStatus.Completed, (await f.Driver.Bundle.State()).WorkItems[new("A")].Status);
    }

    [Fact]
    public async Task InvalidWorkingProvenanceCannotPinTheTaskToAnUnusableCase()
    {
        using var f = await CompletionFixture.OpenAsync();
        var original = f.Policy;
        var host = (FileGovernedTaskService)f.Driver.Bundle.Service;
        Assert.Null((await host.InspectCoordinationAsync(new("T1"), default))?.AssuranceAnchor);
        await f.SavePolicyAsync(original with { Implementer = "another-worker" });
        var error = await Assert.ThrowsAsync<AssuranceRefusal>(() => AILedger.Cli.Assurance.AssuranceHost.OpenAsync(
            f.Driver.Authority, f.Driver.Options.Dispatch.AssuranceStore!,
            new("operator", "invalid-setup", "external-client", null), null, default, host));
        Assert.Equal("wrong_governed_binding", error.Code);
        Assert.Null((await host.InspectCoordinationAsync(new("T1"), default))?.AssuranceAnchor);
        await f.SavePolicyAsync(original with { CaseId = "corrected-case" });
        _ = await AILedger.Cli.Assurance.AssuranceHost.OpenAsync(f.Driver.Authority,
            f.Driver.Options.Dispatch.AssuranceStore!, new("operator", "valid-setup", "external-client", null), null, default, host);
        Assert.Equal("corrected-case", (await host.InspectCoordinationAsync(new("T1"), default))!.AssuranceAnchor!.CaseId);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("expired")]
    [InlineData("role")]
    [InlineData("missing-input")]
    public async Task InvalidAssurancePreparationLeavesNoPermanentAnchor(string invalid)
    {
        using var f = await CompletionFixture.OpenAsync();
        var host = (FileGovernedTaskService)f.Driver.Bundle.Service;
        var policy = f.Policy;
        if (invalid == "missing-input")
            policy = policy with { Areas = policy.Areas.Select(a => a with { CandidatePaths = ["missing.cs"] }).ToArray() };
        else
            policy = policy with { Principals = policy.Principals.Select(p => p.Id != "operator" ? p : p with
            {
                Enabled = invalid != "disabled",
                ExpiresAt = invalid == "expired" ? DateTimeOffset.UtcNow.AddMinutes(-1) : p.ExpiresAt,
                Role = invalid == "role" ? "verification" : p.Role
            }).ToArray() };
        await f.SavePolicyAsync(policy);
        Assert.NotNull(await Record.ExceptionAsync(() => AILedger.Cli.Assurance.AssuranceHost.OpenAsync(
            f.Driver.Authority, f.Driver.Options.Dispatch.AssuranceStore!,
            new("operator", "invalid-setup", "external-client", null), null, default, host)));
        Assert.Null((await host.InspectCoordinationAsync(new("T1"), default))?.AssuranceAnchor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncertainNewerWorkingExecutionCannotPinAssurance(bool failed)
    {
        using var f = await CompletionFixture.OpenAsync();
        await CliStageFixture.AdvanceAsync(f.Driver.Bundle.App, f.Driver.Bundle.Root, "T1", TaskStage.Repair);
        await f.Driver.Bundle.Ok("run", "start", "--subject", "worker", "--run", "newer-work", "--work", "A", "--provider", "codex");
        if (failed)
            await f.Driver.Bundle.Ok("run", "complete", "--run", "newer-work", "--status", "failed", "--session", "failed-working-session");
        var host = (FileGovernedTaskService)f.Driver.Bundle.Service;
        await Assert.ThrowsAsync<GovernanceException>(() => AILedger.Cli.Assurance.AssuranceHost.OpenAsync(
            f.Driver.Authority, f.Driver.Options.Dispatch.AssuranceStore!,
            new("operator", "uncertain-work", "external-client", null), null, default, host));
        Assert.Null((await host.InspectCoordinationAsync(new("T1"), default))?.AssuranceAnchor);
    }

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
