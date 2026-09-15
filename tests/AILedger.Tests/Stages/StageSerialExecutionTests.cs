using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Stages;

public sealed class StageSerialExecutionTests
{
    private static readonly DateTimeOffset When = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SerialJustificationIsRequiredOnlyForSerializableFanout()
    {
        var single = ExecutionWithWork(("W1", "single"));
        single.RecordWorkingPass(new WorkItemId("W1"), "RW1");
        single.Transition(TaskStage.Verification);
        Assert.Equal(TaskStage.Verification, single.State.Stage);

        var serial = ExecutionWithWork(("W1", "serial-a"), ("W2", "serial-b"));
        serial.RecordWorkingPass(new WorkItemId("W1"), "RW1");
        serial.RecordWorkingPass(new WorkItemId("W2"), "RW2");
        var unexplained = Assert.Throws<GovernanceException>(() => serial.Transition(TaskStage.Verification));
        Assert.Contains("entirely serial", unexplained.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Execution, serial.State.Stage);

        serial.Apply(new RecordAlternativeCommand(
            serial.OperatorId, null, serial.NextCorrelation(), new AlternativeId("ALT-serial"),
            "Dispatch the disjoint items concurrently", "One item depended on a scarce local fixture", null));
        var explained = serial.Apply(new RequestStageTransitionCommand(
            serial.OperatorId, null, serial.NextCorrelation(), TaskStage.Verification,
            SerialJustification: new AlternativeId("ALT-serial")));
        var transition = Assert.IsType<StageTransitioned>(Assert.Single(explained.Events).Data);
        Assert.Equal(new AlternativeId("ALT-serial"), transition.SerialJustification);

        var overlapping = ExecutionWithWork(("W1", "overlap-a"), ("W2", "overlap-b"));
        overlapping.RecordWorkingPass(new WorkItemId("W1"), "RW1");
        overlapping.RecordWorkingPass(new WorkItemId("W2"), "RW2");
        var sharedScope = overlapping.State.WorkItems[new WorkItemId("W1")].ResourceScope;
        var replaced = overlapping.State.WorkItems[new WorkItemId("W2")] with
        {
            ResourceScope = sharedScope
        };
        var overlappingState = overlapping.State with
        {
            WorkItems = overlapping.State.WorkItems.Values
                .Select(item => item.Id == replaced.Id ? replaced : item)
                .ToDictionary(item => item.Id)
        };
        var overlappingOutcome = new CommandHandler().Handle(
            overlappingState,
            new RequestStageTransitionCommand(
                overlapping.OperatorId, null, "overlap-verification", TaskStage.Verification),
            When);
        Assert.Equal(TaskStage.Verification, overlappingOutcome.State.Stage);

        var concurrent = ExecutionWithWork(("W1", "parallel-a"), ("W2", "parallel-b"));
        var worker = new ActorId("worker");
        concurrent.Apply(new StartRunCommand(
            concurrent.OperatorId, null, concurrent.NextCorrelation(), new RunId("RW1"),
            new WorkItemId("W1"), "codex", null, null, null, null, worker));
        concurrent.Apply(new StartRunCommand(
            concurrent.OperatorId, null, concurrent.NextCorrelation(), new RunId("RW2"),
            new WorkItemId("W2"), "codex", null, null, null, null, worker));
        concurrent.Apply(new CompleteRunCommand(
            concurrent.OperatorId, null, concurrent.NextCorrelation(), new RunId("RW1"),
            AgentRunStatus.Completed, "session-RW1"));
        concurrent.Apply(new CompleteRunCommand(
            concurrent.OperatorId, null, concurrent.NextCorrelation(), new RunId("RW2"),
            AgentRunStatus.Completed, "session-RW2"));
        concurrent.Transition(TaskStage.Verification);
        Assert.Equal(TaskStage.Verification, concurrent.State.Stage);
    }

    private static TestTask ExecutionWithWork(params (string Id, string Scope)[] items)
    {
        var task = new TestTask(placeEntryStages: false);
        task.ReachStage(TaskStage.Ready);
        task.Assign(new ActorId("worker"), RoleKind.Worker, Capability.BuildContext, Capability.RecordArtifact);
        foreach (var item in items)
        {
            task.Apply(Work(task, new WorkItemId(item.Id), [Path.GetFullPath(item.Scope)]));
        }
        task.RecordUserRequest();
        task.Transition(TaskStage.Execution);
        return task;
    }

    private static AddWorkItemCommand Work(TestTask task, WorkItemId workItemId, string[] scope) =>
        new(task.OperatorId, null, task.NextCorrelation(), workItemId, "Build", null, [], scope);
}
