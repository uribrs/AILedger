using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Cli;
using AILedger.Tests.Support;

namespace AILedger.Tests.Assurance;

public sealed partial class BundleAssuranceTests
{
    [Fact]
    public async Task ArtifactAuthorityAndPlanPrerequisiteRemainIndependentGates()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Ok("run", "start", "--run", "VAB", "--subject", "verifier", "--work", "A", "--also-work", "B", "--candidate", BundleFixture.Candidate, "--provider", "claude");
        var state = await f.State();
        var command = new RecordArtifactCommand(new ActorId("verifier"), null, "filing", new ArtifactId("OUT-VAB"),
            GovernedArtifactKind.VerifierOutput, "result", ArtifactCommands.VerifierBody, null, new RunId("VAB"), null);
        var handler = new CommandHandler();
        // State-only omission isolates the document prerequisite; no event log is rewritten.
        var noPlan = state with { Artifacts = state.Artifacts.Where(p => p.Value.Kind != GovernedArtifactKind.OrchestrationPlan).ToDictionary(p => p.Key, p => p.Value) };
        var missingPlan = Assert.Throws<GovernanceException>(() => handler.Handle(noPlan, command, DateTimeOffset.UtcNow));
        Assert.Contains("orchestration plan", missingPlan.Message, StringComparison.Ordinal);
        var wrongActor = Assert.Throws<GovernanceException>(() => handler.Handle(state, command with { ActorId = new ActorId("reviewer") }, DateTimeOffset.UtcNow));
        Assert.Contains("produc", wrongActor.Message, StringComparison.OrdinalIgnoreCase);
        await CliStageFixture.ToReviewAsync(f.App, f.Root);
        var reviewStage = await f.State();
        var wrongKind = Assert.Throws<GovernanceException>(() => handler.Handle(reviewStage, command with { Kind = GovernedArtifactKind.CodeReviewOutput }, DateTimeOffset.UtcNow));
        Assert.Contains("matching active CodeReviewer run", wrongKind.Message, StringComparison.Ordinal);
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Repair);
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Verification);
        Assert.Equal(0, await f.File("verifier", "VAB", "OUT-VAB", "verifier-output", "--work", "B", "--also-work", "A"));
        await f.Ok("run", "complete", "--run", "VAB", "--status", "completed", "--session", "manual-session");
    }

    [Fact]
    public async Task NewVerifierInvalidatesOldPairedReviewOnlyForReplacedMember()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Verify("VAB", "A", "B");
        await f.Review("RAB", "VAB", "A", "B");
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Repair);
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Verification);
        await f.Verify("VA", "A");
        Assert.Equal(1, await f.Run("work", "complete", "--id", "A"));
        Assert.Contains("review", f.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        await f.Ok("work", "complete", "--id", "B");
        Assert.Equal(WorkItemStatus.Completed, (await f.State()).WorkItems[new WorkItemId("B")].Status);
    }

    [Fact]
    public async Task LegacyAssuranceCannotBypassNewApplicability()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Verify("VAB", "A", "B");
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["B"], candidate: null), "candidate");
    }

    [Fact]
    public async Task BindingArraysAreCopiedBeforePersistence()
    {
        using var f = await BundleFixture.CreateAsync();
        var binding = await f.Binding("A", "B");
        var members = binding.WorkItemIds.ToArray();
        var versions = binding.WorkVersions.ToArray();
        var command = new StartRunCommand(new ActorId("operator"), null, "copy", new RunId("COPY"), new WorkItemId("A"), "claude", null,
            SubjectActorId: new ActorId("verifier"), Assurance: binding with { WorkItemIds = members, WorkVersions = versions });
        var outcome = new CommandHandler().Handle(await f.State(), command, DateTimeOffset.UtcNow);
        members[1] = new WorkItemId("C");
        versions[1] = versions[1] with { WorkItemId = new WorkItemId("C") };
        var started = Assert.Single(outcome.Events.Select(e => e.Data).OfType<RunStarted>());
        Assert.Equal(new[] { "A", "B" }, started.Run.Assurance!.WorkItemIds.Select(i => i.Value));
        Assert.Equal(new[] { "A", "B" }, started.Run.Assurance.WorkVersions.Select(i => i.WorkItemId.Value));
    }
}
