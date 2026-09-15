using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class CommandValidationTests
{
    [Fact]
    public void CompletionMustBeTerminalAndPreserveSessionIdentity()
    {
        var task = new TestTask();
        var run = new RunId("R1");
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), run, null, "codex", "session-1"));

        Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, AgentRunStatus.Active, "session-1")));
        Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, AgentRunStatus.Completed, "different")));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, AgentRunStatus.Completed, "session-1"));

        Assert.Equal(AgentRunStatus.Completed, task.State.Runs[run].Status);
    }

    [Fact]
    public void UndefinedCommandEnumsAreRejectedAtCoreBoundary()
    {
        var task = new TestTask();

        Assert.Throws<GovernanceException>(() => task.Apply(new AssignRoleCommand(
            task.OperatorId, null, task.NextCorrelation(), new ActorId("worker"), (RoleKind)999, [])));
        Assert.Throws<GovernanceException>(() => task.Apply(new AssignRoleCommand(
            task.OperatorId, null, task.NextCorrelation(), new ActorId("worker"), RoleKind.Worker, [(Capability)999])));
        Assert.Throws<GovernanceException>(() => task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), (TaskStage)999)));
    }
}
