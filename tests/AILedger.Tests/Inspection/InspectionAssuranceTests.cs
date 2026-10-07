using AILedger.Core.Contracts;
using AILedger.Core.Inspection;
using AILedger.Core.Artifacts;
using AILedger.Storage;
using AILedger.Tests.Alternatives;
using AILedger.Tests.Assurance;
using AILedger.Tests.Support;
using AILedger.Tests.Cli;

namespace AILedger.Tests.Inspection;

[Collection(AlternativesRecordingCollection.Name)]
public sealed class InspectionAssuranceTests
{
    [Fact]
    public async Task RelevantSelectionIncludesDependenciesAndTaskSelectionRetrievesOtherCurrentClaims()
    {
        using var f = await BundleFixture.CreateAsync(dependencies: true);
        await f.Ok("run", "start", "--run", "VA", "--subject", "verifier", "--work", "A", "--candidate", BundleFixture.Candidate, "--provider", "claude");
        var b = new InspectionBinding(new("T1"), new("verifier"), new("VA"), "VA", AllowInspect: true);
        var service = (ITaskInspector)f.Service;
        var index = await service.InspectAsync(b, new(Limit: 32), default);
        Assert.Equal("ok", index.Status);
        Assert.Contains(index.Records, r => r.Kind == "Claim" && r.Id == "CA");
        Assert.Contains(index.Records, r => r.Kind == "Evidence" && r.Id == "EA");
        Assert.DoesNotContain(index.Records, r => r.Kind == "Claim" && r.Id == "CB");
        var task = await service.InspectAsync(b, new(Selection: "task", Limit: 32), default);
        Assert.Contains(task.Records, r => r.Kind == "Claim" && r.Id == "CB");
    }

    [Fact]
    public async Task BoundReviewerCannotEscapeIsolationAndAssuranceReadinessNeverPretendsPhysicalInspection()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Verify("VAB", "A", "B");
        await CliStageFixture.ToReviewAsync(f.App, f.Root);
        await f.Ok("run", "start", "--run", "RAB", "--subject", "reviewer", "--work", "A", "--also-work", "B", "--candidate", BundleFixture.Candidate, "--verifier-run", "VAB", "--provider", "codex");
        var service = (ITaskInspector)f.Service;
        var b = new InspectionBinding(new("T1"), new("reviewer"), new("RAB"), "RAB", AllowInspect: true, AllowSubmitArtifact: true);
        foreach (var selection in new[] { "relevant", "task" })
        {
            var index = await service.InspectAsync(b, new(Selection: selection, Limit: 32), default);
            Assert.Equal("ok", index.Status);
            Assert.Equal(BundleFixture.Candidate, index.Snapshot!.CandidateId);
            Assert.DoesNotContain(index.Records, r => r.Kind is "Claim" or "Evidence" or "TaskGoal" or "VerifierOutput" or "OrchestrationPlan");
            var forbidden = await service.RetrieveAsync(b, new(1, selection, "VerifierOutput", "OUT-VAB", index.Snapshot.LedgerVersion), default);
            Assert.Equal("not_visible", forbidden.Diagnostic!.Code);
        }
        var version = (await f.State()).Version;
        var query = new ReadinessQuery(1, "submit_artifact", version,
            Artifact: new(1, "review", GovernedArtifactKind.CodeReviewOutput, "Review", "Independent review findings"));
        var before = await InspectionTests.Files(Path.Combine(f.Root, "T1"));
        var readiness = await service.CheckReadinessAsync(b, query, default);
        Assert.Equal("unknown", readiness.Status); Assert.Equal("candidate_uninspected", readiness.Diagnostic!.Code);
        Assert.Equal(before, await InspectionTests.Files(Path.Combine(f.Root, "T1")));
    }

    [Fact]
    public async Task DependencyChangeBetweenReadinessAndExecutionCannotCompleteStaleWork()
    {
        using var f = await BundleFixture.CreateAsync(dependencies: true);
        await f.Verify("VA", "A"); await f.Review("RA", "VA", "A");
        var b = new InspectionBinding(new("T1"), new("operator"), null, "inspect", AllowInspect: true, AllowRunless: true);
        var query = new ReadinessQuery(1, "complete_work", (await f.State()).Version, WorkId: "A");
        var service = (ITaskInspector)f.Service;
        var missingAcceptance = await service.CheckReadinessAsync(b, query, default);
        Assert.Equal("blocked", missingAcceptance.Status);
        Assert.Contains("Explicit applicable task-13 acceptance", missingAcceptance.Diagnostic!.Reason);
        await f.Ok("evidence", "add", "--id", "REFUTE", "--source-type", "fixture", "--citation", "fixture.cs:1", "--summary", "Changed input", "--refutes", "CA");
        await f.Ok("claim", "resolve", "--id", "CA", "--status", "rejected", "--evidence", "REFUTE");
        Assert.Equal("stale_snapshot", (await service.CheckReadinessAsync(b, query, default)).Diagnostic!.Code);
        var blocked = await service.CheckReadinessAsync(b, query with { ExpectedVersion = (await f.State()).Version }, default);
        Assert.Equal("blocked", blocked.Status);
        Assert.NotEqual(0, await f.Run("work", "complete", "--id", "A"));
        Assert.Contains(blocked.Diagnostic!.Reason, f.Error.ToString());
        Assert.Equal(WorkItemStatus.Stale, (await f.State()).WorkItems[new("A")].Status);
    }
}
