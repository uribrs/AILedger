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

    // A work item can no longer be completed until a verifier has passed over it. Tests that pin
    // some other rule still have to get past that gate, so they record the pass the way the kernel
    // does: an operator dispatches a run to an actor holding the verifier role, and closes it.
    public RunId RecordVerifierPass(WorkItemId workItemId, string runId = "RV") =>
        RecordPass(workItemId, runId, new ActorId("verifier"), RoleKind.Verifier);

    // Completion also requires a completed run by a role that does the work, so an item nobody ever
    // worked on cannot be declared finished. Staged the same way, under a worker.
    public RunId RecordWorkingPass(WorkItemId workItemId, string runId = "RW") =>
        RecordPass(workItemId, runId, new ActorId("worker"), RoleKind.Worker);

    // Both runs a completion needs. Tests whose subject is some other rule call this and stop
    // caring how many gates completion has grown; only the tests that pin a gate stage one alone.
    public void RecordRequiredRuns(WorkItemId workItemId)
    {
        RecordWorkingPass(workItemId);
        RecordVerifierPass(workItemId);
    }

    private RunId RecordPass(WorkItemId workItemId, string runId, ActorId subject, RoleKind role)
    {
        if (!State.Roles.ContainsKey(subject))
        {
            Assign(subject, role, Capability.BuildContext);
        }

        var run = new RunId(runId);
        Apply(new StartRunCommand(
            OperatorId, null, NextCorrelation(), run, workItemId, "codex", null, null, null, null, subject));
        Apply(new CompleteRunCommand(
            OperatorId, null, NextCorrelation(), run, AgentRunStatus.Completed, $"session-{runId}"));
        return run;
    }
}
