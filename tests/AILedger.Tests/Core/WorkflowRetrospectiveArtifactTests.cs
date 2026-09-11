using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// The retrospective is the one governed artifact filed after the task is over, by an actor holding
// no run, about the agents that worked it. Three properties carry that: it names no producer run,
// it is refused until nothing on the task is live, and its body carries the frozen dimension table.
// A fourth is the absence tested in ArtifactContextTests: no manifest of any role ever carries it.
public sealed class WorkflowRetrospectiveArtifactTests
{
    [Fact]
    public void R2_AnOperatorFilesARetrospectiveWithNoProducerRun()
    {
        var task = Archived();

        var outcome = task.Apply(Retrospective(task, task.OperatorId, "A-retro"));

        var artifact = outcome.State.Artifacts[new ArtifactId("A-retro")];
        Assert.Equal(GovernedArtifactKind.WorkflowRetrospective, artifact.Kind);
        Assert.Null(artifact.ProducerRunId);
        // Task-wide, on the UserRequest branch.
        Assert.Null(artifact.WorkItemId);
    }

    [Fact]
    public void R2_NamingAProducerRunIsRefused()
    {
        var task = Archived();
        var run = new RunId("R-retro");
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, null, "codex",
            null, null, null, null, task.OperatorId));

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Retrospective(task, task.OperatorId, "A-retro", producerRun: run)));

        // An operator-held run carries no provider session and can never close 'completed', so
        // requiring one would spend a permanently cancelled run on every task the feature measures.
        Assert.Equal(
            "A workflow-retrospective artifact is recorded after closeout and cannot name a producer run.",
            error.Message);
    }

    [Theory]
    [InlineData(RoleKind.Worker)]
    [InlineData(RoleKind.Verifier)]
    [InlineData(RoleKind.CodeReviewer)]
    [InlineData(RoleKind.Researcher)]
    public void OnlyAnOperatorOrALeadMayRecordOne(RoleKind role)
    {
        var task = Archived();
        var actor = new ActorId("actor-" + role);
        task.Assign(actor, role, Capability.BuildContext, Capability.RecordArtifact);

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Retrospective(task, actor, "A-retro")));

        Assert.Equal(
            "Only an operator, planning lead, or implementation lead can record a governing workflow artifact.",
            error.Message);
    }

    [Theory]
    [InlineData(RoleKind.PlanningLead)]
    [InlineData(RoleKind.ImplementationLead)]
    public void EitherLeadMayRecordOne(RoleKind role)
    {
        var task = Archived();
        var actor = new ActorId("actor-" + role);
        task.Assign(actor, role, Capability.BuildContext, Capability.RecordArtifact);

        var outcome = task.Apply(Retrospective(task, actor, "A-retro"));

        Assert.True(outcome.State.Artifacts.ContainsKey(new ArtifactId("A-retro")));
    }

    // The three refusals of the entry condition, each naming which of the three conditions failed.
    // The wording is frozen surface: the CLI help line quotes it and so does this test.
    [Fact]
    public void R1_RecordIsRefusedBeforeArchive()
    {
        var task = new TestTask();

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Retrospective(task, task.OperatorId, "A-retro")));

        Assert.Equal(
            "A workflow retrospective is recorded only at stage 'Archive'; this task is at stage 'Discovery'.",
            error.Message);
    }

    [Fact]
    public void R1_RecordIsRefusedWhileAWorkItemIsLive()
    {
        var task = Archived(completeWork: false);

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Retrospective(task, task.OperatorId, "A-retro")));

        // Paused, not Active: live is every status that still holds a directory area, which is the
        // same set ScopeOccupancyRules refuses an overlapping scope against.
        Assert.Equal(
            "A workflow retrospective is recorded only when no work item is live; 'W-stage' is still " +
            "live in status 'Paused'.",
            error.Message);
    }

    [Fact]
    public void R1_RecordIsRefusedWhileARunIsActive()
    {
        var task = Archived();
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R-late"), null, "codex",
            null, null, null, null, new ActorId("researcher")));

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Retrospective(task, task.OperatorId, "A-retro")));

        Assert.Equal(
            "A workflow retrospective is recorded only when no run is active; run 'R-late' is still active.",
            error.Message);
    }

    [Fact]
    public void ABodyWithNoDimensionTableIsRefused()
    {
        var task = Archived();

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Retrospective(task, task.OperatorId, "A-retro", body: "# Retrospective\n\nIt went well.\n")));

        Assert.Contains("dimension | score | confidence | controllable | evidence", error.Message,
            StringComparison.Ordinal);
    }

    // The property every other item in this wave is written around. The kernel reads the score cell
    // to test that it is in the vocabulary and for nothing else, so the worst possible retrospective
    // is recorded exactly as the best one is and changes the outcome of nothing.
    //
    // Each case builds its own uniform body rather than taking the shared one, and the shared one
    // carries none of these three values. That is what makes the test isolable: a gate that refused
    // a body carrying a '0' must fail this case and no other, and while the shared fixture scored
    // every dimension '0' it failed fourteen cases that say nothing about scores at all.
    [Theory]
    [InlineData("0")]
    [InlineData("5")]
    [InlineData("unmeasured")]
    public void NoScoreChangesTheOutcomeOfAnything(string score)
    {
        var task = Archived();

        var outcome = task.Apply(Retrospective(task, task.OperatorId, "A-retro", body: Table(score)));

        Assert.True(outcome.State.Artifacts.ContainsKey(new ArtifactId("A-retro")));
        Assert.Equal(TaskStage.Archive, outcome.State.Stage);
        Assert.All(outcome.State.WorkItems.Values,
            item => Assert.Equal(WorkItemStatus.Completed, item.Status));
    }

    [Theory]
    [InlineData("6")]
    [InlineData("excellent")]
    public void AScoreOutsideTheFrozenVocabularyIsRefused(string score)
    {
        var task = Archived();

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Retrospective(task, task.OperatorId, "A-retro", body: Table(score))));

        Assert.Contains("outside the frozen vocabulary", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("| D1 | 3 | certain | not-applicable | refusals.total = 0 |")]
    [InlineData("| D1 | 3 | low | maybe | refusals.total = 0 |")]
    [InlineData("| D1 | 3 | low | not-applicable |  |")]
    public void AConfidenceControllableOrEvidenceCellOutsideTheVocabularyIsRefused(string row)
    {
        var task = Archived();
        var body = Table().Replace(Row(1), row, StringComparison.Ordinal);

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Retrospective(task, task.OperatorId, "A-retro", body: body)));

        Assert.Contains("row 'D1' uses a value outside the frozen vocabulary", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingDimensionIsRefused()
    {
        var task = Archived();
        var body = Table().Replace(Row(7) + "\n", "", StringComparison.Ordinal);

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Retrospective(task, task.OperatorId, "A-retro", body: body)));

        Assert.Contains("must score dimension 'D7'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADimensionOutsideD1ToD10IsRefused()
    {
        var task = Archived();
        var body = Table() + "| D11 | 3 | low | not-applicable | refusals.total = 0 |\n";

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Retrospective(task, task.OperatorId, "A-retro", body: body)));

        Assert.Contains("scores 'D11', which is not a dimension", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADimensionScoredTwiceIsRefused()
    {
        var task = Archived();
        var body = Table() + "| D3 | 4 | high | yes | refusals.total = 0 |\n";

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Retrospective(task, task.OperatorId, "A-retro", body: body)));

        Assert.Contains("Workflow retrospective dimension IDs cannot contain duplicates", error.Message,
            StringComparison.Ordinal);
    }

    // Both rule copies. The command-time copy accepted the artifact above; the replay copy is what
    // reads it back, and a history the kernel wrote must survive being read again.
    [Fact]
    public void TheRecordedRetrospectiveReplays()
    {
        var task = Archived();
        task.Apply(Retrospective(task, task.OperatorId, "A-retro"));

        var reducer = new TaskReducer();
        GovernedTaskState? replayed = null;
        foreach (var item in task.Events)
        {
            replayed = reducer.Apply(replayed, item);
        }

        Assert.NotNull(replayed);
        Assert.Equal(
            GovernedArtifactKind.WorkflowRetrospective,
            replayed!.Artifacts[new ArtifactId("A-retro")].Kind);
    }

    // The ten scores of the shared body. They are varied, and none of them is '0', '5' or
    // 'unmeasured' — the three values NoScoreChangesTheOutcomeOfAnything builds a body of its own
    // for. Every test in this wave that does not care about scores reads this body, so a value that
    // appears here cannot be mutated against without taking those tests down too: a fixture scored
    // uniformly '0' made the gate-on-zero mutation fail fifteen cases instead of one, and a fixture
    // scored uniformly anything else would move that same problem to another value.
    private static readonly string[] SharedScores = ["3", "1", "4", "2", "3", "1", "4", "2", "1", "3"];

    // One row of the shared body, so a test that removes or replaces a row names it by dimension
    // rather than by repeating its text — the text now differs per dimension.
    internal static string Row(int dimension) =>
        "| D" + dimension + " | " + SharedScores[dimension - 1] + " | low | not-applicable | " +
        "refusals.total = 0 |";

    // With no argument, the shared varied body. With one, a uniform body of that score, which is
    // what a test asserting something about a particular score asks for.
    internal static string Table(string? score = null)
    {
        var rows = string.Empty;
        for (var index = 1; index <= 10; index++)
        {
            rows += score is null
                ? Row(index) + "\n"
                : "| D" + index + " | " + score + " | low | not-applicable | refusals.total = 0 |\n";
        }

        return "# Retrospective\n\n" +
            "| dimension | score | confidence | controllable | evidence |\n" +
            "| --- | --- | --- | --- | --- |\n" +
            rows;
    }

    internal static RecordArtifactCommand Retrospective(
        TestTask task,
        ActorId actor,
        string artifactId,
        string? body = null,
        RunId? producerRun = null) =>
        ArtifactCommands.Record(
            task, actor, artifactId, GovernedArtifactKind.WorkflowRetrospective,
            body ?? Table(), "How this task was governed", producerRun: producerRun);

    // An archived task with nothing live on it: the only state in which a retrospective may be
    // recorded, and therefore the starting point of almost every test in this file.
    internal static TestTask Archived(bool completeWork = true)
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Learn);
        if (completeWork)
        {
            foreach (var item in task.State.WorkItems.Values
                         .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                         .ToArray())
            {
                task.Apply(new CompleteWorkItemCommand(
                    task.OperatorId, null, task.NextCorrelation(), item.Id));
            }
        }

        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT-stage",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["retrospective"],
            Verify: "ailedger status --task task-1",
            DoNot: "Do not walk the stages without recording what was considered",
            Actor: LessonActor.Verifier, Kind: LessonKind.Workflow,
            VerifyExpects: VerifyExpectation.Present));
        task.Transition(TaskStage.Archive);
        return task;
    }
}
