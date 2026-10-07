using System.Security.Cryptography;
using System.Text.Json;
using AILedger.Cli.Assurance;
using AILedger.Cli.Orchestration;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Handoffs;
using AILedger.Providers.Assurance;
using AILedger.Storage;
using AILedger.Tests.Cli;
using AILedger.Tests.Support;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Tests.Orchestration;

[Collection(StandardInput.Collection)]
public sealed class AcceptanceCompletionTests
{
    [Fact]
    public async Task IndependentEvidenceExplicitAcceptanceAndRestartCompleteThroughRestrictedDriver()
    {
        using var f = await CompletionFixture.OpenAsync();
        var pending = await f.RunAsync();
        Assert.Equal(DriverStatus.AwaitingAcceptance, pending.Status);
        Assert.Equal("missing_acceptance", pending.Code);
        Assert.Equal(2, pending.Dispatches.Count);
        var accepted = await f.AcceptAsync();
        Assert.Null(accepted.Error);
        var replay = await f.AcceptService!.InvokeAsync("accept_assurance", Json(f.AcceptRequest), default);
        Assert.True(replay.Replayed); Assert.Equal(accepted.Receipt!.Id, replay.Receipt!.Id);
        var resumed = await f.RunAsync();
        Assert.Empty(resumed.Dispatches);
        var state = await f.Driver.Bundle.State();
        Assert.Equal(WorkItemStatus.Completed, state.WorkItems[new("A")].Status);
        Assert.Equal(TaskStage.Learn, state.Stage);
        // Other members remain unresolved, so this is work completion, not task/archive acceptance.
        Assert.Equal(DriverStatus.AwaitingAcceptance, resumed.Status);
        Assert.NotEqual(WorkItemStatus.Completed, state.WorkItems[new("B")].Status);
    }

    [Fact]
    public async Task FindingRequiresIndependentDriverAdjudicationThenExplicitAcceptanceBeforeCompletion()
    {
        using var f = await CompletionFixture.OpenAsync();
        await f.RunAsync();
        var refused = await f.AcceptAsync(withFinding: true);
        Assert.Equal("unresolved_disagreement", refused.Error?.Code);
        var pending = await f.RunAsync(withFindings: true);
        Assert.Equal(DriverStatus.AwaitingAcceptance, pending.Status);
        Assert.Equal("unresolved_findings", pending.Code);
        Assert.Single(pending.Dispatches);
        Assert.NotNull(f.SynthesisReceipt);
        Assert.NotEqual(WorkItemStatus.Completed, (await f.Driver.Bundle.State()).WorkItems[new("A")].Status);
        var decision = f.AcceptRequest! with
        {
            RequestId = "accept-refutation",
            Reports = f.AcceptRequest!.Reports.Append(f.SynthesisReceipt!).ToArray(),
            Dispositions = [new(f.FindingId!, "refuted", "Independent source trace contradicts the original assertion", [f.SynthesisReceipt!])]
        };
        var accepted = await f.AcceptService!.InvokeAsync("accept_assurance", Json(decision), default);
        Assert.True(accepted.Error is null, Json(accepted).GetRawText());
        var completed = await f.RunAsync();
        Assert.Empty(completed.Dispatches);
        Assert.Equal(WorkItemStatus.Completed, (await f.Driver.Bundle.State()).WorkItems[new("A")].Status);
    }

    [Fact]
    public async Task DependentAreaNeedsBothExplicitAcceptancesOnSameGovernedBasis()
    {
        using var f = await CompletionFixture.OpenAsync(withDependency: true);
        var pending = await f.RunAsync();
        Assert.True(pending.Status == DriverStatus.AwaitingAcceptance, pending.Code + ": " + pending.Diagnostic);
        Assert.Equal("dependency_reassessment", (await f.AcceptAsync(area: "b")).Error?.Code);
        Assert.Null((await f.AcceptAsync()).Error);
        await Assert.ThrowsAsync<AssuranceRefusal>(() => f.CompleteAsync());
        Assert.Null((await f.AcceptAsync(area: "b")).Error);
        await f.RunAsync();
        Assert.Equal(WorkItemStatus.Completed, (await f.Driver.Bundle.State()).WorkItems[new("A")].Status);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task BuiltCliReplaysAcceptanceAndCompletesWithoutDispatchingAnotherProvider(bool completionAlreadyCommitted)
    {
        using var f = await CompletionFixture.OpenAsync();
        await f.RunAsync(); var accepted = await f.AcceptAsync(); Assert.Null(accepted.Error);
        var replay = await RunCliAsync(["assurance", "accept_assurance", "--authority", f.Driver.Authority,
            "--store", f.Driver.Options.Dispatch.AssuranceStore!, "--governed-root", f.Driver.Bundle.Root,
            "--principal", "operator", "--session", "trusted-acceptance-session", "--body-stdin"],
            JsonSerializer.Serialize(f.AcceptRequest, HandoffJson.Options));
        Assert.True(replay.Exit == 0, replay.Error + replay.Output);
        Assert.Contains(accepted.Receipt!.Id, replay.Output);
        Assert.Contains("\"replayed\":true", replay.Output);
        if (completionAlreadyCommitted) await f.CompleteAsync();
        var profiles = Path.Combine(f.Driver.Protected.Path, "completion-profiles.json");
        await File.WriteAllTextAsync(profiles, "{\"schema_version\":1,\"agents\":[{\"work\":\"verification\",\"subject\":\"verifier\",\"provider\":\"claude\"}]}");
        var result = await RunCliAsync(["orchestrate", "run", "--root", f.Driver.Bundle.Root, "--task", "T1", "--actor", "operator",
            "--work", "A", "--profiles", profiles, "--working-directory", f.Driver.Bundle.Repository,
            "--cognitive-root", f.Driver.Options.Dispatch.CognitiveRoot!, "--assurance-authority", f.Driver.Authority,
            "--assurance-store", f.Driver.Options.Dispatch.AssuranceStore!, "--acceptance-principal", "operator"], null);
        Assert.True(result.Exit == 4, result.Error + result.Output); // Other members still need acceptance.
        Assert.Equal(WorkItemStatus.Completed, (await f.Driver.Bundle.State()).WorkItems[new("A")].Status);
        Assert.Contains("completion_boundary", result.Output);
    }

    [Fact]
    public async Task PhysicalChangeAfterAdmissionObservationIsCaughtBeforeAppend()
    {
        using var f = await CompletionFixture.OpenAsync();
        await f.RunAsync(); Assert.Null((await f.AcceptAsync()).Error);
        var admission = new GovernedCompletionHost(f.Driver.Authority, f.Driver.Options.Dispatch.AssuranceStore!, "operator", f.Driver.Bundle.Root);
        var host = ((FileGovernedTaskService)f.Driver.Bundle.Service).BindCompletionAdmission(new BeforeCommitAdmission(admission,
            () => File.AppendAllTextAsync(Path.Combine(f.Policy.CandidateRoot, "a.cs"), "// changed concurrently")));
        await Assert.ThrowsAsync<AssuranceRefusal>(() => host.ExecuteAsync(new("T1"),
            new CompleteWorkItemCommand(new("operator"), null, "raced-completion", new("A")), default));
        Assert.NotEqual(WorkItemStatus.Completed, (await f.Driver.Bundle.State()).WorkItems[new("A")].Status);
    }

    [Theory]
    [InlineData("wrong_task")] [InlineData("wrong_work")]
    public async Task PolicyCannotAssociateEvidenceWithDifferentTaskOrWork(string failure)
    {
        using var f = await CompletionFixture.OpenAsync();
        await f.RunAsync(); Assert.Null((await f.AcceptAsync()).Error);
        await f.SavePolicyAsync(f.Policy with { Governed = failure == "wrong_task" ? new(1, "another-task", ["A"]) : new(1, "T1", ["B"]) });
        await Assert.ThrowsAsync<AssuranceRefusal>(() => f.CompleteAsync());
    }

    internal static async Task<(int Exit, string Output, string Error)> RunCliAsync(string[] args, string? input,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(typeof(AILedger.Cli.CliApplication).Assembly.Location);
        foreach (var argument in args) start.ArgumentList.Add(argument);
        foreach (var variable in environment ?? new Dictionary<string, string>()) start.Environment[variable.Key] = variable.Value;
        using var process = System.Diagnostics.Process.Start(start)!;
        if (input is not null) await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    private sealed class BeforeCommitAdmission(IWorkCompletionAdmission inner, Func<Task> before) : IWorkCompletionAdmission
    {
        public async Task<IWorkCompletionLease> AcquireAsync(GovernedTaskState state, WorkItemId work, CancellationToken token) =>
            new BeforeCommitLease(await inner.AcquireAsync(state, work, token), before);
    }
    private sealed class BeforeCommitLease(IWorkCompletionLease inner, Func<Task> before) : IWorkCompletionLease
    {
        public GovernedAcceptanceReceipt Receipt => inner.Receipt;
        public async Task RevalidateAsync(CancellationToken token) { await before(); await inner.RevalidateAsync(token); }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    [Theory]
    [InlineData("candidate")] [InlineData("requirements")] [InlineData("policy")]
    [InlineData("revocation")] [InlineData("reassignment")] [InlineData("failed_work")] [InlineData("cancelled_work")]
    public async Task AcceptedReceiptCannotCompleteChangedOrUncertainBasis(string change)
    {
        using var f = await CompletionFixture.OpenAsync();
        await f.RunAsync(); Assert.Null((await f.AcceptAsync()).Error);
        if (change is "candidate" or "requirements")
            await File.AppendAllTextAsync(Path.Combine(f.Policy.CandidateRoot, change == "candidate" ? "a.cs" : "a.txt"), "changed");
        if (change == "policy") await f.SavePolicyAsync(f.Policy with { CaseId = "different-case" });
        if (change == "revocation") await f.SavePolicyAsync(f.Policy with { Principals = f.Policy.Principals.Select(p => p.Id == "requirements-review" ? p with { Enabled = false } : p).ToArray() });
        if (change == "reassignment") await f.Driver.Bundle.Ok("actor", "attach", "--target", "requirements-review", "--role", "researcher");
        if (change is "failed_work" or "cancelled_work")
        {
            await CliStageFixture.BackAsync(f.Driver.Bundle.App, f.Driver.Bundle.Root, TaskStage.Repair);
            await f.Driver.Bundle.Ok("run", "start", "--run", "uncertain", "--subject", "worker", "--work", "A", "--provider", "codex");
            await File.AppendAllTextAsync(Path.Combine(f.Policy.CandidateRoot, "a.cs"), "// uncertain physical change");
            await f.Driver.Bundle.Ok("run", "complete", "--run", "uncertain", "--status", change == "failed_work" ? "failed" : "cancelled");
        }
        var refusal = await Assert.ThrowsAnyAsync<Exception>(() => f.CompleteAsync());
        Assert.True(refusal is GovernanceException or AssuranceRefusal, refusal.ToString());
        Assert.NotEqual(WorkItemStatus.Completed, (await f.Driver.Bundle.State()).WorkItems[new("A")].Status);
    }

    [Theory]
    [InlineData("fail")] [InlineData("unknown")] [InlineData("not_checked")]
    public async Task HonestNegativeEvidenceIsRecordableButCannotBeAccepted(string status)
    {
        using var f = await CompletionFixture.OpenAsync();
        await f.RunAsync();
        var accepted = await f.AcceptAsync(status);
        Assert.Equal("incomplete_assurance", accepted.Error?.Code);
        var refusal = await Assert.ThrowsAnyAsync<Exception>(() => f.CompleteAsync());
        Assert.True(refusal is GovernanceException or AssuranceRefusal, refusal.ToString());
    }

    [Fact]
    public async Task PrivilegedCliAndForgedSerializedAdmissionCannotBypassOwningHost()
    {
        using var f = await CompletionFixture.OpenAsync();
        await f.RunAsync(); Assert.Null((await f.AcceptAsync()).Error);
        Assert.Equal(1, await f.Driver.Bundle.Run("work", "complete", "--id", "A"));
        var state = await f.Driver.Bundle.State();
        await Assert.ThrowsAsync<GovernanceException>(() => f.Driver.Bundle.Service.ExecuteAsync(new("T1"),
            new CompleteWorkItemCommand(new("operator"), null, "forged", new("A")), default));
        Assert.DoesNotContain("accept_assurance", f.Driver.Bundle.Adapter.Requests.Last().FindingsEndpoint!.AssuranceTools ?? []);
    }
}

internal sealed class CompletionFixture : IDisposable
{
    internal DriverFixture Driver { get; private set; } = null!;
    internal AssurancePolicy Policy { get; private set; } = null!;
    internal Dictionary<string, string> VerificationReceipts { get; } = new();
    internal string? SynthesisReceipt { get; private set; }
    internal string? FindingId { get; private set; }
    internal AssuranceService? AcceptService { get; private set; }
    internal AcceptAssuranceRequest? AcceptRequest { get; private set; }
    internal static async Task<CompletionFixture> OpenAsync(bool withDependency = false, bool independentAreas = false)
    {
        var f = new CompletionFixture { Driver = await DriverFixture.OpenAsync() };
        await f.Driver.Bundle.Ok("actor", "attach", "--target", "requirements-review", "--role", "planning-lead");
        await f.Driver.Bundle.Ok("actor", "attach", "--target", "synthesis", "--role", "planning-lead");
        var old = HandoffJson.ParseDocument<AssurancePolicy>(await File.ReadAllTextAsync(f.Driver.Authority));
        var program = "/bin/sh";
        var sha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(program))).ToLowerInvariant();
        f.Policy = old with
        {
            Implementer = "worker", Areas = withDependency ? old.Areas.Take(2).Select(a => independentAreas ? a with { DependsOnAreas = [] } : a).ToArray() : [old.Areas[0]], Governed = new(1, "T1", ["A"]),
            Principals = [new("verifier", "verification", true, DateTimeOffset.UtcNow.AddHours(1), withDependency ? ["a", "b"] : ["a"]),
                new("requirements-review", "review", true, DateTimeOffset.UtcNow.AddHours(1), withDependency ? ["a", "b"] : ["a"]),
                new("synthesis", "synthesis", true, DateTimeOffset.UtcNow.AddHours(1), withDependency ? ["a", "b"] : ["a"]),
                new("operator", "acceptance", true, DateTimeOffset.UtcNow.AddHours(1), withDependency ? ["a", "b"] : ["a"])],
            Checks = [new("unit", program, sha, ["-c", "for source in *.cs; do test \"$(cat \"$source\")\" = 'return 1;' || exit 1; done"], 5)]
        };
        await f.SavePolicyAsync(f.Policy);
        f.Driver.Bundle.Adapter.BeforeResult = async request =>
        {
            var role = (await f.Driver.Bundle.State()).Runs[request.RunId].SubjectRole;
            using var relay = await CognitiveRelay.OpenAsync(request.FindingsEndpoint!);
            if (role == RoleKind.Verifier)
            foreach (var area in f.Policy.Areas.Select(a => a.Id))
            {
                var inspected = await relay.CallAsync("inspect_assurance", Json(new InspectAssuranceRequest(1, area)));
                Assert.True(inspected.GetProperty("status").GetString() == "ok", inspected.GetRawText());
                var snapshot = inspected.GetProperty("data").Deserialize<AssuranceInspection>(HandoffJson.Options)!.Snapshot;
                var read = await relay.CallAsync("read_assurance", Json(new ReadAssuranceRequest(1, request.RunId.Value + "-read-" + area, area, snapshot.BindingSha256, snapshot.Inputs.Select(i => i.Path).ToArray())));
                var checks = await relay.CallAsync("run_assurance_checks", Json(new RunAssuranceChecksRequest(1, request.RunId.Value + "-check-" + area, area, snapshot.BindingSha256, ["unit"])));
                Assert.True(checks.GetProperty("status").GetString() == "recorded", checks.GetRawText());
                var report = await relay.CallAsync("record_assurance", Json(new RecordAssuranceRequest(1, request.RunId.Value + "-report-" + area, area, snapshot.BindingSha256,
                    "complete", "Verified bounded return-one fixture", snapshot.Inputs.Select(i => i.Path).ToArray(), [read.GetProperty("receipt").GetProperty("id").GetString()!],
                    [new("behavior", "pass", "Actual shell check on captured input", [checks.GetProperty("receipt").GetProperty("id").GetString()!])], [], [], f.VerificationReceipts.GetValueOrDefault(area))));
                Assert.True(report.GetProperty("status").GetString() == "recorded", report.GetRawText());
                f.VerificationReceipts[area] = report.GetProperty("receipt").GetProperty("id").GetString()!;
            }
            if (role == RoleKind.PlanningLead)
            {
                var inspected = await relay.CallAsync("inspect_assurance", Json(new InspectAssuranceRequest(1, "a")));
                var snapshot = inspected.GetProperty("data").Deserialize<AssuranceInspection>(HandoffJson.Options)!.Snapshot;
                var read = await relay.CallAsync("read_assurance", Json(new ReadAssuranceRequest(1, "synthesis-read", "a", snapshot.BindingSha256, snapshot.Inputs.Select(i => i.Path).ToArray())));
                var report = await relay.CallAsync("record_assurance", Json(new RecordAssuranceRequest(1, "synthesis-report", "a", snapshot.BindingSha256,
                    "complete", "The source returns one, contradicting the reviewer assertion that it returns zero", snapshot.Inputs.Select(i => i.Path).ToArray(),
                    [read.GetProperty("receipt").GetProperty("id").GetString()!], [new("behavior", "pass", "Read exact current source and requirement", [])], [], [])
                    { FindingJudgments = [new(1, f.FindingId!, "refuted", "Observed return 1 contradicts return 0 assertion", ["behavior"], [],
                        [new("source_trace", read.GetProperty("receipt").GetProperty("id").GetString()!, "a.cs", "return 1;")])] }));
                Assert.True(report.GetProperty("status").GetString() == "recorded", report.GetRawText());
                f.SynthesisReceipt = report.GetProperty("receipt").GetProperty("id").GetString();
            }
            await relay.AssessAsync(role == RoleKind.Verifier ? CognitiveWorkKind.Verification :
                role == RoleKind.PlanningLead ? CognitiveWorkKind.Findings : CognitiveWorkKind.Review);
            await relay.FinishAsync();
        };
        return f;
    }
    internal Task SavePolicyAsync(AssurancePolicy policy) => File.WriteAllTextAsync(Driver.Authority, JsonSerializer.Serialize(policy, HandoffJson.Options));
    internal async Task<DriverResult> RunAsync(bool withFindings = false)
    {
        var profiles = new Dictionary<CognitiveWorkKind, DriverAgentProfile>(Driver.Options.Agents);
        if (withFindings) profiles[CognitiveWorkKind.Findings] = new(new("synthesis"), "codex");
        return await (await Driver.Driver(Driver.Options with { AcceptancePrincipal = "operator", Agents = profiles })).DriveAsync(new(new("A")), default);
    }
    internal async Task<AssuranceResponse> AcceptAsync(string checkStatus = "pass", bool withFinding = false, string area = "a")
    {
        var host = (FileGovernedTaskService)Driver.Bundle.Service;
        var store = Driver.Options.Dispatch.AssuranceStore!;
        var review = await AssuranceHost.OpenAsync(Driver.Authority, store, new("requirements-review", "independent-requirements-session", "external-client", null), null, default, host);
        var inspected = await review.InvokeAsync("inspect_assurance", Json(new InspectAssuranceRequest(1, area)), default);
        Assert.Null(inspected.Error);
        var snapshot = inspected.Data!.Value.Deserialize<AssuranceInspection>(HandoffJson.Options)!.Snapshot;
        var read = await review.InvokeAsync("read_assurance", Json(new ReadAssuranceRequest(1, "requirements-read-" + area, area, snapshot.BindingSha256, snapshot.Inputs.Select(i => i.Path).ToArray())), default);
        Assert.Null(read.Error);
        var report = await review.InvokeAsync("record_assurance", Json(new RecordAssuranceRequest(1, "requirements-report-" + area, area, snapshot.BindingSha256,
            "complete", "Requirement-aware independent inspection", snapshot.Inputs.Select(i => i.Path).ToArray(), [read.Receipt!.Id],
            [new("behavior", checkStatus, "Captured return-one requirement and implementation", [])],
            withFinding ? [new("wrong-return", "defect", "Implementation returns zero", ["a.cs"], []) { RequirementIds = ["behavior"], Uncertainty = [] }] : [], [])), default);
        Assert.Null(report.Error);
        if (withFinding) FindingId = report.Receipt!.Id + ":wrong-return";
        AcceptService = await AssuranceHost.OpenAsync(Driver.Authority, store, new("operator", "trusted-acceptance-session", "external-client", null), null, default, host);
        AcceptRequest = new(1, "explicit-acceptance-" + area, area, snapshot.BindingSha256, [report.Receipt!.Id, VerificationReceipts[area]], "Explicit acceptance of independently verified current candidate", []);
        return await AcceptService.InvokeAsync("accept_assurance", Json(AcceptRequest), default);
    }
    internal async Task<CommandOutcome> CompleteAsync()
    {
        var host = ((FileGovernedTaskService)Driver.Bundle.Service).BindCompletionAdmission(new GovernedCompletionHost(Driver.Authority,
            Driver.Options.Dispatch.AssuranceStore!, "operator", Driver.Bundle.Root));
        return await host.ExecuteAsync(new("T1"), new CompleteWorkItemCommand(new("operator"), null, "complete", new("A")), default);
    }
    public void Dispose() => Driver.Dispose();
}
