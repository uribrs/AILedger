using AILedger.Core.Application;
using AILedger.Core.Contracts;

namespace AILedger.Tests.Support;

internal sealed class TestTask
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly CommandHandler _handler = new();
    private int _commandNumber;

    public TestTask(string taskId = "task-1", string operatorId = "operator")
    {
        TaskId = new TaskId(taskId);
        OperatorId = new ActorId(operatorId);
        Apply(new OpenTaskCommand(OperatorId, null, NextCorrelation(), TaskId, "Test task", "Prove governed execution"));
    }

    public TaskId TaskId { get; }
    public ActorId OperatorId { get; }
    public GovernedTaskState State { get; private set; } = null!;

    public CommandOutcome Apply(LedgerCommand command)
    {
        var outcome = _handler.Handle(State, command, Epoch.AddMinutes(_commandNumber));
        State = outcome.State;
        return outcome;
    }

    public string NextCorrelation() => $"correlation-{++_commandNumber}";

    public void Assign(ActorId actorId, RoleKind role, params Capability[] capabilities) =>
        Apply(new AssignRoleCommand(
            OperatorId,
            null,
            NextCorrelation(),
            actorId,
            role,
            capabilities));
}
