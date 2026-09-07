using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// The two places the artifacts stop being a record and start being a rule. Entering Execution
// requires the three task-wide documents to exist, and a verifier or code reviewer cannot close a
// successful pass without the output that pass was dispatched to produce. Both are command-time
// only; ArtifactReplayBoundaryTests pins that they never reach the replay validator.
public sealed class ArtifactGateTests
{
    // The user request is the only one of the three that can still be missing at Ready: the Scope
    // arm asks for the contract and the Ready arm asks for the plan, so a task that reached Ready
    // has both. Those two halves of this gate are pinned in StagePrerequisiteTests, against the
    // stages that now demand them first.
    [Fact]
    public void ExecutionCannotBeEnteredWithoutTheUserRequestContractAndPlan()
    {
        var task = ReadyToExecute();

        var missingRequest = Assert.Throws<GovernanceException>(() => Transition(task, TaskStage.Execution));
        // The refusal names what is missing, because the operator's next command depends on it.
        Assert.Contains(nameof(GovernedArtifactKind.UserRequest), missingRequest.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Ready, task.State.Stage);

        task.Apply(ArtifactCommands.Record(task, task.OperatorId, "A1", GovernedArtifactKind.UserRequest));
        Transition(task, TaskStage.Execution);

        Assert.Equal(TaskStage.Execution, task.State.Stage);
    }

    // The gate asks for a current document, not for one that was recorded once. A revised contract
    // satisfies it; the superseded predecessor on its own would not.
    [Fact]
    public void TheGateReadsTheCurrentRevisionOfEachKind()
    {
        var task = ReadyToExecute();
        task.RecordExecutionArtifacts();
        var revisionRun = task.StartArtifactProducer(task.OperatorId, RoleKind.Operator, "RP-revision");
        task.Apply(ArtifactCommands.Record(
            task, task.OperatorId, "A-contract-2", GovernedArtifactKind.PromptContract,
            "The revised contract", producerRun: revisionRun, supersedes: "A-contract"));

        Transition(task, TaskStage.Execution);

        Assert.Equal(TaskStage.Execution, task.State.Stage);
    }

    [Fact]
    public void AVerifierRunCannotBeClosedAsCompletedWithoutItsFindings()
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        ArtifactRecordTests.AddWork(task, workItemId, "w1");
        ArtifactRecordTests.StartVerifierRun(task, workItemId, "RV", out var run);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, AgentRunStatus.Completed, "session-RV")));

        Assert.Contains(nameof(GovernedArtifactKind.VerifierOutput), error.Message, StringComparison.Ordinal);
        Assert.Equal(AgentRunStatus.Active, task.State.Runs[run].Status);
    }

    [Fact]
    public void AVerifierRunClosesOnceItsFindingsAreOnTheRecord()
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        ArtifactRecordTests.AddWork(task, workItemId, "w1");
        var verifier = ArtifactRecordTests.StartVerifierRun(task, workItemId, "RV", out var run);
        task.Apply(ArtifactCommands.Record(
            task, verifier, "A1", GovernedArtifactKind.VerifierOutput, ArtifactCommands.VerifierBody,
            workItem: workItemId, producerRun: run));

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, AgentRunStatus.Completed, "session-RV"));

        Assert.Equal(AgentRunStatus.Completed, task.State.Runs[run].Status);
    }

    // A pass that died has no findings to file, and demanding them would leave the run stuck open
    // forever. The rule is about a successful pass, not about every way a run can end.
    [Theory]
    [InlineData(AgentRunStatus.Failed)]
    [InlineData(AgentRunStatus.Cancelled)]
    public void AVerifierRunThatDidNotSucceedNeedsNoFindings(AgentRunStatus status)
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        ArtifactRecordTests.AddWork(task, workItemId, "w1");
        ArtifactRecordTests.StartVerifierRun(task, workItemId, "RV", out var run);

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, status, null));

        Assert.Equal(status, task.State.Runs[run].Status);
    }

    // The artifact has to belong to the run being closed. Otherwise one verifier's findings would
    // close every later pass on the same work item, which is the stale-pass hole in another shape.
    [Fact]
    public void FindingsFromAnEarlierPassDoNotCloseALaterOne()
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        ArtifactRecordTests.AddWork(task, workItemId, "w1");
        task.RecordVerifierPass(workItemId, "RV-first");
        ArtifactRecordTests.StartVerifierRun(task, workItemId, "RV-second", out var second);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), second, AgentRunStatus.Completed, "session-2")));

        Assert.Contains(nameof(GovernedArtifactKind.VerifierOutput), error.Message, StringComparison.Ordinal);
        Assert.Equal(AgentRunStatus.Active, task.State.Runs[second].Status);
    }

    [Fact]
    public void ACodeReviewerRunCannotBeClosedAsCompletedWithoutItsReview()
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        ArtifactRecordTests.AddWork(task, workItemId, "w1");
        task.RecordWorkingPass(workItemId);
        task.RecordVerifierPass(workItemId);
        var reviewer = new ActorId("reviewer");
        task.Assign(reviewer, RoleKind.CodeReviewer, Capability.BuildContext, Capability.RecordArtifact);
        var run = new RunId("RR");
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, workItemId, "claude",
            null, null, null, null, reviewer));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, AgentRunStatus.Completed, "session-RR")));
        Assert.Contains(nameof(GovernedArtifactKind.CodeReviewOutput), error.Message, StringComparison.Ordinal);

        task.Apply(ArtifactCommands.Record(
            task, reviewer, "A1", GovernedArtifactKind.CodeReviewOutput, "Review findings",
            workItem: workItemId, producerRun: run));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, AgentRunStatus.Completed, "session-RR"));

        Assert.Equal(AgentRunStatus.Completed, task.State.Runs[run].Status);
    }

    // The control that keeps the two gates above honest: the roles that build things close their
    // runs exactly as they always did. The rule reaches the two inspecting roles and no further.
    [Fact]
    public void AWorkingRunClosesWithNoArtifactAtAll()
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        ArtifactRecordTests.AddWork(task, workItemId, "w1");
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.BuildContext);
        var run = new RunId("RW");
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, workItemId, "codex",
            null, null, null, null, worker));

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, AgentRunStatus.Completed, "session-RW"));

        Assert.Equal(AgentRunStatus.Completed, task.State.Runs[run].Status);
        Assert.Empty(task.State.Artifacts);
    }

    private static TestTask ReadyToExecute()
    {
        var task = new TestTask();
        ArtifactRecordTests.AddWork(task, new WorkItemId("W1"), "w1");
        // Each stage on the way records what its own arm asks for, which by Ready is the contract,
        // the plan and the roles the rest of the walk needs — and not the user request.
        task.ReachStage(TaskStage.Ready);
        return task;
    }

    private static void Transition(TestTask task, TaskStage stage) =>
        task.Apply(new RequestStageTransitionCommand(task.OperatorId, null, task.NextCorrelation(), stage));
}
