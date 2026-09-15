using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Stages;

public sealed class StageLifecycleTests
{
    [Fact]
    public void LifecycleAllowsForwardPipelineAndGovernedBackwardRepair()
    {
        var task = new TestTask();
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Build",
            null, [], [Path.GetFullPath("src")]));

        task.ReachStage(TaskStage.Verification);
        task.RecordVerifierPass(task.StageWorkItem());
        Transition(task, TaskStage.Repair);
        const string sentBack =
            "The verifier's findings are in the implementation, not in the checks: the work item " +
            "goes back to Execution to be rewritten rather than patched in Repair.";
        Transition(task, TaskStage.Execution, sentBack);

        Assert.Equal(TaskStage.Execution, task.State.Stage);
        var sendBack = Assert.IsType<StageTransitioned>(task.Events[^1].Data);
        Assert.Equal(sentBack, sendBack.Reason);
    }

    [Fact]
    public void LifecycleRejectsSkippedStage()
    {
        var task = new TestTask();

        var exception = Assert.Throws<GovernanceException>(() => Transition(task, TaskStage.Ready));

        Assert.Contains("not legal", exception.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Discovery, task.State.Stage);
    }

    [Fact]
    public void ArchiveRejectsOpenChallenge()
    {
        var task = new TestTask();
        task.Apply(new AddClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), new ClaimId("C1"), "Claim", null));
        task.Apply(new RaiseChallengeCommand(
            task.OperatorId, null, task.NextCorrelation(), new ChallengeId("CH1"), "claim", "C1",
            "Uncertain", []));
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Build",
            null, [], [Path.GetFullPath("src")]));
        task.ReachStage(TaskStage.Learn);
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT-stage",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["stages"],
            Verify: "dotnet test --filter StagePrerequisiteTests",
            DoNot: "Do not bypass the governed stage walk", Actor: LessonActor.Verifier,
            VerifyExpects: VerifyExpectation.Present));

        var exception = Assert.Throws<GovernanceException>(() => Transition(task, TaskStage.Archive));

        Assert.Contains("open challenges", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIllegalTransitionNamesTheStagesThatAreLegalFromHere()
    {
        var task = new TestTask();

        var error = Assert.Throws<GovernanceException>(() => Transition(task, TaskStage.Archive));

        Assert.Contains(
            "Transition from 'Discovery' to 'Archive' is not legal",
            error.Message,
            StringComparison.Ordinal);
        Assert.Contains("legal from 'Discovery': 'Research'", error.Message, StringComparison.Ordinal);
        Assert.Equal([TaskStage.Research], StageTransitionPolicy.LegalTargets(TaskStage.Discovery));
    }

    [Fact]
    public void EveryStageCanNameItsLegalTargetsAndArchiveSaysItIsTerminal()
    {
        foreach (var stage in Enum.GetValues<TaskStage>())
        {
            var legal = StageTransitionPolicy.LegalTargets(stage);
            Assert.All(legal, target => Assert.True(StageTransitionPolicy.CanTransition(stage, target)));
            Assert.All(
                Enum.GetValues<TaskStage>().Where(target => !legal.Contains(target)),
                target => Assert.False(StageTransitionPolicy.CanTransition(stage, target)));
            Assert.Equal(legal.OrderBy(target => target), legal);
        }

        Assert.Empty(StageTransitionPolicy.LegalTargets(TaskStage.Archive));
        var terminal = Assert.Throws<GovernanceException>(
            () => StageTransitionPolicy.EnsureAllowed(TaskStage.Archive, TaskStage.Learn));
        Assert.Contains("'Archive' is terminal", terminal.Message, StringComparison.Ordinal);
    }

    private static void Transition(TestTask task, TaskStage stage, string? reason = null) =>
        task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), stage, Reason: reason));
}
