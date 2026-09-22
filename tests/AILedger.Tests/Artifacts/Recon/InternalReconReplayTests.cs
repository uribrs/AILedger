using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Artifacts.Recon;

public sealed class InternalReconReplayTests
{
    private static GovernedTaskState Replay(IEnumerable<LedgerEvent> events)
    {
        var reducer = new TaskReducer();
        GovernedTaskState? state = null;
        foreach (var item in events) state = reducer.Apply(state, item);
        return state!;
    }

    [Fact]
    public void R4_OldDesignWithoutReconStillReplays()
    {
        var f = new InternalReconFixture();
        var reducer = new TaskReducer();
        var state = Replay(f.Task.Events);
        var legacy = new LedgerEvent(GovernedTaskState.CurrentSchemaVersion, new EventId("legacy-design"),
            state.TaskId, f.Task.OperatorId, DateTimeOffset.UtcNow, null, "legacy",
            new StageTransitioned(TaskStage.Research, TaskStage.Design));
        Assert.Equal(TaskStage.Design, reducer.Apply(state, legacy).Stage);
        Assert.Empty(state.Artifacts);
    }

    [Fact]
    public void ReconReplaysAtFilingPositionDespiteLaterClaimChanges()
    {
        var f = new InternalReconFixture();
        f.Eligible();
        f.Resolve();
        var replayed = Replay(f.Task.Events);
        Assert.Equal(f.Task.State.Artifacts[new ArtifactId("recon")].Content,
            replayed.Artifacts[new ArtifactId("recon")].Content);
        Assert.Equal(ClaimStatus.Validated, replayed.Claims[new ClaimId("C-topic")].Status);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("producer")]
    [InlineData("owner")]
    [InlineData("task")]
    [InlineData("hash")]
    [InlineData("coverage")]
    [InlineData("work")]
    public void ForgedNewReconEventIsRejected(string defect)
    {
        var f = new InternalReconFixture();
        f.Start();
        var prefix = f.Task.Events.ToArray();
        f.File();
        var recorded = f.Task.Events.Last();
        var artifact = ((ArtifactRecorded)recorded.Data).Artifact;
        artifact = defect switch
        {
            "body" => artifact with { Content = "{}" },
            "task" => artifact with { Content = artifact.Content.Replace("\"task-1\"", "\"wrong-task\"") },
            "hash" => artifact with { Content = artifact.Content.Replace(
                InternalReconDocuments.ComputeClaimSetHash(f.Task.State), new string('0', 64)) },
            "coverage" => artifact with { Content = artifact.Content.Replace("C-topic", "unknown") },
            "work" => artifact with { WorkItemId = new WorkItemId("absent") },
            "producer" => artifact with { ProducerRunId = new RunId("absent") },
            _ => artifact with { Provenance = artifact.Provenance with { ActorId = f.Task.OperatorId } }
        };
        var reducer = new TaskReducer();
        var state = Replay(prefix);
        Assert.Throws<GovernanceException>(() => reducer.Apply(state, recorded with { Data = new ArtifactRecorded(artifact) }));
    }
    [Fact]
    public void ExistingArtifactEnumValuesRemainStable()
    {
        Assert.Equal(0, (int)GovernedArtifactKind.UserRequest);
        Assert.Equal(1, (int)GovernedArtifactKind.PromptContract);
        Assert.Equal(2, (int)GovernedArtifactKind.OrchestrationPlan);
        Assert.Equal(3, (int)GovernedArtifactKind.VerifierOutput);
        Assert.Equal(4, (int)GovernedArtifactKind.CodeReviewOutput);
        Assert.Equal(5, (int)GovernedArtifactKind.WorkflowRetrospective);
        Assert.Equal(6, (int)GovernedArtifactKind.CloseoutSynthesis);
        Assert.Equal(7, (int)GovernedArtifactKind.InternalRecon);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("work")]
    [InlineData("assurance")]
    public void R4_ReconReplayRejectsIneligibleHistoricalProducer(string defect)
    {
        var f = new InternalReconFixture();
        f.Start();
        var prefix = f.Task.Events.ToArray();
        f.File();
        var state = Replay(prefix);
        var run = state.Runs[f.Run];
        var forged = defect switch
        {
            "role" => run with { SubjectRole = RoleKind.Researcher },
            "work" => run with { WorkItemId = new WorkItemId("historical-work") },
            _ => run with { Assurance = new AssuranceBinding(1, [new WorkItemId("historical-work")],
                [new AssuranceWorkVersion(new WorkItemId("historical-work"), new RunId("working"))],
                "candidate") }
        };
        // Negative provenance probe at the artifact's replay boundary, never used for admission.
        var runs = state.Runs.ToDictionary(pair => pair.Key, pair => pair.Value);
        runs[f.Run] = forged;
        state = state with { Runs = runs };
        Assert.Throws<GovernanceException>(() => new TaskReducer().Apply(state, f.Task.Events.Last()));
    }

}
