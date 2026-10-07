using System.Text.Json;
using AILedger.Cli.Assurance;
using AILedger.Cli.Orchestration;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Handoffs;
using AILedger.Providers.Assurance;
using AILedger.Storage;
using AILedger.Tests.Cli;
using AILedger.Tests.Support;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Tests.Orchestration;

[Collection(StandardInput.Collection)]
public sealed class RepairContinuationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CompletedWorkResumesLearnWithoutDuplicateCompletionAndRejectsChangedInputs(bool changed)
    {
        using var f = await CompletionFixture.OpenAsync();
        await f.RunAsync(); Assert.Null((await f.AcceptAsync()).Error);
        await f.CompleteAsync(); // Simulates death/lost reply after append, before Learn.
        Assert.Equal(TaskStage.Review, (await f.Driver.Bundle.State()).Stage);
        if (changed) await File.AppendAllTextAsync(Path.Combine(f.Policy.CandidateRoot, "a.cs"), "changed");
        var result = await f.RunAsync();
        Assert.Empty(result.Dispatches);
        Assert.Equal(changed ? TaskStage.Review : TaskStage.Learn, (await f.Driver.Bundle.State()).Stage);
        var completed = 0;
        await foreach (var e in f.Driver.Bundle.Service.GetHistoryAsync(new("T1"), default))
            if (e.Data is WorkItemCompleted { WorkItemId.Value: "A" }) completed++;
        Assert.Equal(1, completed);
        if (changed) Assert.Equal(DriverStatus.AwaitingAcceptance, result.Status);
    }

    [Fact]
    public async Task RepairedAreaInvalidatesItsReviewWhileIndependentUnchangedAreaReusesOriginalReview()
    {
        using var f = await CompletionFixture.OpenAsync(withDependency: true, independentAreas: true);
        await f.RunAsync(); Assert.Null((await f.AcceptAsync()).Error); var oldA = f.AcceptRequest!.Reports[0];
        Assert.Null((await f.AcceptAsync(area: "b")).Error); var oldB = f.AcceptRequest!.Reports[0];
        var oldVerifier = f.VerificationReceipts["b"];
        await RepairAsync(f);
        var synthesis = await OpenAsync(f, "synthesis", "impact-session");
        var changed = await ImpactAsync(synthesis, "a", oldA, affected: ["a"], reuse: [], invalidate: [oldA]);
        Assert.Null(changed.Error);
        var reuse = await ImpactAsync(synthesis, "b", oldB, affected: ["a"], reuse: [oldB], invalidate: []);
        Assert.True(reuse.Error is null, Json(reuse).GetRawText());
        var replay = await synthesis.InvokeAsync("record_assurance", reuse.Data!.Value.GetProperty("report"), default);
        Assert.True(replay.Replayed); Assert.Equal(reuse.Receipt!.Id, replay.Receipt!.Id);
        var cliReplay = await AcceptanceCompletionTests.RunCliAsync(["assurance", "record_assurance", "--authority", f.Driver.Authority,
            "--store", f.Driver.Options.Dispatch.AssuranceStore!, "--governed-root", f.Driver.Bundle.Root,
            "--principal", "synthesis", "--session", "impact-session", "--body-stdin"], reuse.Data.Value.GetProperty("report").GetRawText());
        Assert.True(cliReplay.Exit == 0, cliReplay.Error + cliReplay.Output);
        Assert.Contains(reuse.Receipt.Id, cliReplay.Output);
        var acceptor = await OpenAsync(f, "operator", "new-acceptance");
        var snapshot = await InspectAsync(acceptor, "b");
        Assert.Equal("current", snapshot.History.Single(h => h.Id == oldB).Status);
        Assert.Equal("reassessment_required", snapshot.History.Single(h => h.Id == oldVerifier).Status);
        Assert.Equal("not_accepted", snapshot.Acceptance);
        var decision = new AcceptAssuranceRequest(1, "reuse-explicit", "b", snapshot.Snapshot.BindingSha256,
            [oldB, f.VerificationReceipts["b"], reuse.Receipt.Id], "Explicit acceptance of new candidate; original independent review reassessed", []);
        var accepted = await acceptor.InvokeAsync("accept_assurance", Json(decision), default);
        Assert.True(accepted.Error is null, Json(accepted).GetRawText());
        var a = await InspectAsync(acceptor, "a");
        Assert.Equal("reassessment_required", a.History.Single(h => h.Id == oldA).Status);
        await Assert.ThrowsAsync<AssuranceRefusal>(() => f.CompleteAsync()); // Member still needs area A acceptance.
        var original = await acceptor.InvokeAsync("inspect_assurance", Json(new InspectAssuranceRequest(1, "b", oldB)), default);
        Assert.Contains(oldB, original.Data!.Value.GetRawText());
        Assert.NotEqual(f.VerificationReceipts["b"], oldVerifier);
    }

    [Theory]
    [InlineData("dependency")] [InlineData("requirements")] [InlineData("governing")]
    [InlineData("authority")] [InlineData("policy")] [InlineData("uncertainty")]
    [InlineData("missing_dependency")] [InlineData("missing_check")]
    public async Task UnsupportedImpactCannotAssociateOldEvidence(string change)
    {
        using var f = await CompletionFixture.OpenAsync(withDependency: true, independentAreas: change != "dependency" && change != "missing_dependency");
        await f.RunAsync(); Assert.Null((await f.AcceptAsync()).Error); Assert.Null((await f.AcceptAsync(area: "b")).Error);
        var old = f.AcceptRequest!.Reports[0];
        await RepairAsync(f);
        if (change == "requirements") await File.AppendAllTextAsync(Path.Combine(f.Policy.CandidateRoot, "b.txt"), "New criterion meaning");
        if (change == "governing") await f.Driver.Bundle.Ok("constraint", "add", "--id", "NEW", "--statement", "New governing restriction", "--source", "trusted fixture");
        if (change == "authority") await f.Driver.Bundle.Ok("actor", "attach", "--target", "requirements-review", "--role", "researcher");
        if (change == "policy") await f.SavePolicyAsync(f.Policy with { Checks = f.Policy.Checks.Select(c => c with { TimeoutSeconds = 6 }).ToArray() });
        var synthesis = await OpenAsync(f, "synthesis", "impact-session");
        var response = await ImpactAsync(synthesis, "b", old, ["a"], [old], [],
            uncertainty: change == "uncertainty" ? ["Dependency knowledge is incomplete"] : [],
            required: change == "missing_check" ? [] : ["unit"]);
        Assert.NotNull(response.Error);
        Assert.Contains(response.Error.Code, new[] { "unsupported_impact", "unsupported_reuse", "dependency_reassessment", "incomplete_coverage" });
    }

    [Theory]
    [InlineData("failed", false)] [InlineData("failed", true)]
    [InlineData("cancelled", false)] [InlineData("cancelled", true)]
    public async Task InterruptedWorkNeedsPositiveStopEvidenceThenFreshPhysicalAssurance(string status, bool changed)
    {
        using var f = await CompletionFixture.OpenAsync();
        await f.RunAsync(); Assert.Null((await f.AcceptAsync()).Error);
        var oldReview = f.AcceptRequest!.Reports[0];
        await CliStageFixture.BackAsync(f.Driver.Bundle.App, f.Driver.Bundle.Root, TaskStage.Repair);
        await f.Driver.Bundle.Ok("run", "start", "--run", "interrupted-cd", "--subject", "worker", "--work", "A", "--provider", "codex");
        if (changed) await File.AppendAllTextAsync(Path.Combine(f.Policy.CandidateRoot, "a.cs"), "\n");
        await f.Driver.Bundle.Ok("run", "complete", "--run", "interrupted-cd", "--status", status);
        var synthesis = await OpenAsync(f, "synthesis", "reconcile-inputs");
        var unknown = await synthesis.InvokeAsync("inspect_assurance", Json(new InspectAssuranceRequest(1, "a")), default);
        Assert.Equal("governed_admission", unknown.Error?.Code);
        // A retained failure and apparently identical bytes do not prove the process stopped.
        await f.Driver.Bundle.Service.ExecuteAsync(new("T1"), new AddEvidenceCommand(new("operator"), null, "observed-stopped",
            new("STOPPED"), "process-termination", "T1/interrupted-cd", "Controlled fixture execution stopped; no provider or check child remains", [], []), default);
        await RepairAsync(f); // Fresh inspection/working provenance; original receipts remain stale.
        var review = await OpenAsync(f, "requirements-review", "reconciled-review");
        var snapshot = (await InspectAsync(review, "a")).Snapshot;
        var paths = snapshot.Inputs.Select(i => i.Path).ToArray();
        var read = await review.InvokeAsync("read_assurance", Json(new ReadAssuranceRequest(1, "fresh-read", "a", snapshot.BindingSha256, paths)), default);
        var report = await review.InvokeAsync("record_assurance", Json(new RecordAssuranceRequest(1, "fresh-review", "a", snapshot.BindingSha256,
            "complete", "Independent inspection of reconciled physical state", paths, [read.Receipt!.Id],
            [new("behavior", "pass", "Fresh source and current required checks", [])], [], [], oldReview)), default);
        Assert.Null(report.Error);
        var impact = await ImpactAsync(synthesis, "a", oldReview, ["a"], [], [oldReview]);
        Assert.Null(impact.Error);
        var accepting = await OpenAsync(f, "operator", "reconciled-acceptance");
        var decision = await accepting.InvokeAsync("accept_assurance", Json(new AcceptAssuranceRequest(1, "fresh-acceptance", "a", snapshot.BindingSha256,
            [report.Receipt!.Id, f.VerificationReceipts["a"], impact.Receipt!.Id], "Explicit acceptance following confirmed stop and fresh assurance", [])), default);
        Assert.Null(decision.Error);
        await f.CompleteAsync();
        Assert.Equal(WorkItemStatus.Completed, (await f.Driver.Bundle.State()).WorkItems[new("A")].Status);
    }

    internal static async Task RepairAsync(CompletionFixture f)
    {
        if ((await f.Driver.Bundle.State()).Stage != TaskStage.Repair)
            await CliStageFixture.BackAsync(f.Driver.Bundle.App, f.Driver.Bundle.Root, TaskStage.Repair);
        await f.Driver.Bundle.Ok("run", "start", "--run", "repair-cd", "--subject", "worker", "--work", "A", "--provider", "codex");
        await File.AppendAllTextAsync(Path.Combine(f.Policy.CandidateRoot, "a.cs"), "\n");
        await f.Driver.Bundle.Ok("run", "complete", "--run", "repair-cd", "--status", "completed", "--session", "repair-fixture-session");
        await f.Driver.Bundle.Ok("stage", "transition", "--stage", "verification", "--reason", "Repair needs current independent assurance", "--serial-because", "SERIAL");
        var result = await f.RunAsync();
        Assert.True(result.Status == DriverStatus.AwaitingAcceptance, result.Diagnostic);
    }

    internal static Task<AssuranceService> OpenAsync(CompletionFixture f, string principal, string session) =>
        AssuranceHost.OpenAsync(f.Driver.Authority, f.Driver.Options.Dispatch.AssuranceStore!, new(principal, session, "external-client", null),
            null, default, (FileGovernedTaskService)f.Driver.Bundle.Service);

    internal static async Task<AssuranceInspection> InspectAsync(AssuranceService service, string area)
    {
        var inspected = await service.InvokeAsync("inspect_assurance", Json(new InspectAssuranceRequest(1, area)), default);
        Assert.True(inspected.Error is null, Json(inspected).GetRawText());
        return inspected.Data!.Value.Deserialize<AssuranceInspection>(HandoffJson.Options)!;
    }

    internal static async Task<AssuranceResponse> ImpactAsync(AssuranceService service, string area, string before,
        string[] affected, string[] reuse, string[] invalidate, string[]? uncertainty = null, string[]? required = null)
    {
        var snapshot = (await InspectAsync(service, area)).Snapshot;
        var key = Guid.NewGuid().ToString("N");
        var paths = snapshot.Inputs.Select(i => i.Path).ToArray();
        var read = await service.InvokeAsync("read_assurance", Json(new ReadAssuranceRequest(1, key + "-read", area, snapshot.BindingSha256, paths)), default);
        Assert.Null(read.Error);
        var impactReads = new List<string>();
        foreach (var affectedArea in affected.Where(a => a != area))
        {
            var other = (await InspectAsync(service, affectedArea)).Snapshot;
            var inspected = await service.InvokeAsync("read_assurance", Json(new ReadAssuranceRequest(1, key + "-" + affectedArea,
                affectedArea, other.BindingSha256, other.Inputs.Select(i => i.Path).ToArray())), default);
            Assert.Null(inspected.Error); impactReads.Add(inspected.Receipt!.Id);
        }
        return await service.InvokeAsync("record_assurance", Json(new RecordAssuranceRequest(1, key, area, snapshot.BindingSha256,
            "complete", "Inspected repair impact and complete declared dependency boundary", paths, [read.Receipt!.Id],
            [new("behavior", "pass", "Current source trace and requirement inspected", [])], [], [])
        {
            RepairImpact = new(1, before, affected, ["behavior"], [], "Independent area has no semantic use of repaired return expression; declared closure was inspected",
                required ?? ["unit"], [], reuse, invalidate, uncertainty ?? []) { ImpactReadReceipts = impactReads }
        }), default);
    }
}
