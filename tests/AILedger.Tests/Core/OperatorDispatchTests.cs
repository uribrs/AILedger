using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// Starting a run under the actor that will do the work meant a role holding no run authority could
// never be launched — including the verifier and code reviewer, the two the kernel isolates most
// carefully. Dispatch splits the two identities instead of handing those roles ManageRuns.
public sealed class OperatorDispatchTests
{
    [Fact]
    public void AnOperatorCanDispatchARunForARoleThatHoldsNoRunAuthority()
    {
        var task = Prepare(out var workItemId, out var reviewer);
        // A reviewer only starts after a verifier has finished with the item; that ordering rule is
        // pinned in VerificationSequenceTests, and here it is just the precondition of dispatch.
        task.RecordVerifierPass(workItemId);

        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "claude",
            null, null, null, null, reviewer));

        var run = task.State.Runs[new RunId("R1")];
        // The subject does the work and owns the provenance; the operator only authorised it.
        Assert.Equal(reviewer, run.ActorId);
        Assert.Equal(task.OperatorId, run.LaunchedBy);
        Assert.DoesNotContain(Capability.ManageRuns, task.State.Roles[reviewer].Capabilities);
    }

    [Fact]
    public void ANonOperatorCannotDispatchARunForAnotherActor()
    {
        var task = Prepare(out var workItemId, out var reviewer);
        var lead = new ActorId("lead");
        // A planning lead holds ManageRuns, so this is refused for dispatching, not for authority.
        task.Assign(lead, RoleKind.PlanningLead, Capability.ManageRuns);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            lead, null, task.NextCorrelation(), new RunId("R1"), workItemId, "claude",
            null, null, null, null, reviewer)));

        Assert.Contains("on another actor's behalf", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(new RunId("R1"), task.State.Runs.Keys);
    }

    [Fact]
    public void DispatchRefusesASubjectThatIsNotAnActorInTheTask()
    {
        var task = Prepare(out var workItemId, out _);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "claude",
            null, null, null, null, new ActorId("ghost"))));

        Assert.Contains("has no assigned role", error.Message, StringComparison.Ordinal);
    }

    // The ordinary case must be untouched: an actor starting its own run records no dispatcher, so
    // "LaunchedBy is not null" means exactly "dispatched", both here and at replay.
    [Fact]
    public void AnActorStartingItsOwnRunRecordsNoDispatcher()
    {
        var task = Prepare(out var workItemId, out _);

        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "claude", null));

        Assert.Equal(task.OperatorId, task.State.Runs[new RunId("R1")].ActorId);
        Assert.Null(task.State.Runs[new RunId("R1")].LaunchedBy);
    }

    // The second copy of the rule. A forged log claiming a non-operator dispatched a run must be
    // refused at replay, not merely at command time.
    [Fact]
    public void ReplayRefusesADispatchedRunWhoseDispatcherIsNotAnOperator()
    {
        var reviewer = new ActorId("reviewer");
        var lead = new ActorId("lead");
        var recordedAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var state = Replayed(reviewer, lead, recordedAt, out var reducer, out var next);

        var forged = next(state, lead, new RunStarted(new AgentRun(
            new RunId("R1"), reviewer, null, "claude", null, AgentRunStatus.Active, recordedAt, null,
            null, null, null, lead)));

        var error = Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
        Assert.Contains("Only an operator", error.Message, StringComparison.Ordinal);
    }

    // The replay copy of "a dispatched subject must be a real actor in this task". The command copy
    // is pinned above, but only the command copy was: a forged log naming a subject that holds no
    // role would have replayed clean, and every rule that reads the subject's role — the run's
    // recorded SubjectRole, the verifier gate, the reviewer ordering — would then be reasoning
    // about an actor the task never knew. The dispatcher here is a real operator, so authority is
    // satisfied and the ghost subject is the only thing left to refuse.
    [Fact]
    public void ReplayRefusesADispatchedRunWhoseSubjectIsNotAnActorInTheTask()
    {
        var reviewer = new ActorId("reviewer");
        var lead = new ActorId("lead");
        var recordedAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var state = Replayed(reviewer, lead, recordedAt, out var reducer, out var next);
        var operatorId = new ActorId("operator");

        var forged = next(state, operatorId, new RunStarted(new AgentRun(
            new RunId("R1"), new ActorId("ghost"), null, "claude", null, AgentRunStatus.Active, recordedAt,
            null, null, null, null, operatorId)));

        var error = Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
        Assert.Contains("actor role", error.Message, StringComparison.Ordinal);
    }

    // Replay-safety, the mistake this repository has already made twice: a run recorded before
    // dispatch existed carries no dispatcher, and must still read as belonging to its event actor.
    [Fact]
    public void ReplayAcceptsARunRecordedBeforeDispatchExisted()
    {
        var reviewer = new ActorId("reviewer");
        var lead = new ActorId("lead");
        var recordedAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var state = Replayed(reviewer, lead, recordedAt, out var reducer, out var next);

        var legacy = next(state, lead, new RunStarted(new AgentRun(
            new RunId("R1"), lead, null, "claude", null, AgentRunStatus.Active, recordedAt, null)));

        state = reducer.Apply(state, legacy);
        Assert.Equal(lead, state.Runs[new RunId("R1")].ActorId);
        Assert.Null(state.Runs[new RunId("R1")].LaunchedBy);
    }

    private static GovernedTaskState Replayed(
        ActorId reviewer,
        ActorId lead,
        DateTimeOffset recordedAt,
        out TaskReducer reducer,
        out Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next)
    {
        var taskId = new TaskId("dispatch-replay");
        var reducerLocal = new TaskReducer();
        reducer = reducerLocal;
        next = (current, actor, data) => new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{taskId.Value}:{(current?.Version ?? 0) + 1:D10}"),
            taskId, actor, recordedAt, null, "replay", data);

        var op = new ActorId("operator");
        var state = reducerLocal.Apply(null, next(null!, op, new TaskOpened("Dispatch", "Goal")));
        state = reducerLocal.Apply(state, next(state, op, new RoleAssigned(new RoleAssignment(
            op, RoleKind.Operator, Enum.GetValues<Capability>(), new Provenance(op, recordedAt, "task.open")))));
        state = reducerLocal.Apply(state, next(state, op, new RoleAssigned(new RoleAssignment(
            lead, RoleKind.PlanningLead, [Capability.ManageRuns],
            new Provenance(op, recordedAt, "actor.assign-role")))));
        state = reducerLocal.Apply(state, next(state, op, new RoleAssigned(new RoleAssignment(
            reviewer, RoleKind.CodeReviewer, [Capability.BuildContext],
            new Provenance(op, recordedAt, "actor.assign-role")))));
        return state;
    }

    private static TestTask Prepare(out WorkItemId workItemId, out ActorId reviewer)
    {
        var task = new TestTask();
        reviewer = new ActorId("reviewer");
        task.Assign(reviewer, RoleKind.CodeReviewer, Capability.BuildContext, Capability.AddClaim);
        workItemId = new WorkItemId("W1");
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId,
            "Review it", task.OperatorId, [], [Path.GetFullPath("src")]));
        return task;
    }
}
