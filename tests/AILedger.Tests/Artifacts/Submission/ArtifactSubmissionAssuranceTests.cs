using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Tests.Alternatives;
using AILedger.Tests.Assurance;
using AILedger.Tests.Support;
using AILedger.Tests.Cli;

namespace AILedger.Tests.Artifacts.Submission;

[Collection(AlternativesRecordingCollection.Name)]
public sealed class ArtifactSubmissionAssuranceTests
{
    [Fact]
    public async Task BundleOutputsRetainCandidateWorkingVersionsPairingAndMemberSupersessionWithoutCompletingWork()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Ok("run", "start", "--run", "VAB", "--subject", "verifier", "--work", "A", "--also-work", "B", "--candidate", BundleFixture.Candidate, "--provider", "claude");
        var submitter = (IArtifactSubmitter)f.Service;
        var binding = new ArtifactSubmissionBinding(new("T1"), new("verifier"), new("VAB"), "VAB", AllowSubmitArtifact: true);
        var request = new ArtifactSubmissionRequest(1, "bundle", GovernedArtifactKind.VerifierOutput, "Bundle verification", ArtifactCommands.VerifierBody);
        var first = await submitter.SubmitArtifactAsync(binding, request, default); Assert.Null(first.Error);
        Assert.Equal(new[] { "A", "B" }, first.Receipt!.Artifact.CoveredWorkItemIds);
        Assert.Equal(BundleFixture.Candidate, first.Receipt.Artifact.CandidateId);
        Assert.Equal(new[] { "WA", "WB" }, first.Receipt.Artifact.WorkVersions.Select(v => v.WorkingRunId));
        Assert.All((await f.State()).WorkItems.Values, item => Assert.NotEqual(WorkItemStatus.Completed, item.Status));
        await f.Ok("run", "complete", "--run", "VAB", "--status", "completed", "--session", "fixture-verifier");
        await CliStageFixture.ToReviewAsync(f.App, f.Root);
        await f.Ok("run", "start", "--run", "RAB", "--subject", "reviewer", "--work", "A", "--also-work", "B", "--candidate", BundleFixture.Candidate, "--verifier-run", "VAB", "--provider", "codex");
        var reviewBinding = binding with { ActorId = new("reviewer"), RunId = new("RAB"), CorrelationId = "RAB" };
        var review = await submitter.SubmitArtifactAsync(reviewBinding, request with { Kind = GovernedArtifactKind.CodeReviewOutput, Content = "Independent review findings" }, default);
        Assert.Null(review.Error); Assert.Equal("VAB", review.Receipt!.Artifact.VerifierRunId);
        Assert.Equal(new[] { "A", "B" }, review.Receipt.Artifact.CoveredWorkItemIds);
        await f.Ok("run", "complete", "--run", "RAB", "--status", "completed", "--session", "fixture-reviewer");
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Repair);
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Verification);
        await f.Ok("run", "start", "--run", "VA", "--subject", "verifier", "--work", "A", "--candidate", BundleFixture.Candidate, "--provider", "claude");
        var nextBinding = binding with { RunId = new("VA"), CorrelationId = "VA" };
        var bad = await submitter.SubmitArtifactAsync(nextBinding, request with { RequestId = "revision", SupersedesArtifactId = review.Receipt.Artifact.ArtifactId }, default);
        Assert.Equal("kernel_refused", bad.Error?.Code);
        var next = await submitter.SubmitArtifactAsync(nextBinding, request with { RequestId = "revision", SupersedesArtifactId = first.Receipt.Artifact.ArtifactId }, default);
        Assert.Null(next.Error);
        Assert.Null(next.Receipt!.Artifact.SupersedesArtifactId); // Domain replacement is member-specific.
        var edge = Assert.Single(next.Receipt.Artifact.MemberReplacements);
        Assert.Equal("A", edge.WorkItemId); Assert.Contains(first.Receipt.Artifact.ArtifactId, edge.ArtifactIds);
        Assert.True((await submitter.SubmitArtifactAsync(binding, request, default)).Replayed);
    }
}
