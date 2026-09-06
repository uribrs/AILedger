using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class ScopeOccupancyTests
{
    [Fact]
    public void ALiveWorkItemOccupiesItsAreaAgainstOverlappingClaims()
    {
        var task = new TestTask();
        var core = Path.GetFullPath("src/AILedger.Core");
        var nested = Path.Combine(core, "Domain");
        Add(task, "W1", core);

        // Same area, a parent of it, and a child of it are all the same occupied area.
        Assert.Contains("already held by work item 'W1'",
            Assert.Throws<GovernanceException>(() => Add(task, "W2", core)).Message, StringComparison.Ordinal);
        Assert.Throws<GovernanceException>(() => Add(task, "W3", nested));
        Assert.Throws<GovernanceException>(() => Add(task, "W4", Path.GetFullPath("src")));

        // A sibling area is free.
        Add(task, "W5", Path.GetFullPath("src/AILedger.Cli"));
        Assert.Equal(2, task.State.WorkItems.Count);
    }

    [Fact]
    public void CompletingOrInvalidatingAWorkItemReleasesItsArea()
    {
        var task = new TestTask();
        var area = Path.GetFullPath("src/AILedger.Core");
        Add(task, "W1", area);
        // Completion is only scaffolding here — the subject is the area being released — so the
        // operator waives the verifier pass instead of staging one.
        task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(),
            new WorkItemId("W1"), "This test is about scope release, not verification"));

        Add(task, "W2", area);

        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[new WorkItemId("W1")].Status);
        Assert.Equal(WorkItemStatus.Proposed, task.State.WorkItems[new WorkItemId("W2")].Status);
    }

    [Fact]
    public void ABlockedWorkItemStillHoldsItsArea()
    {
        var task = new TestTask();
        var area = Path.GetFullPath("src/AILedger.Core");
        Add(task, "W1", area);
        task.Apply(new BlockWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Waiting on the operator", null));

        // A blocked item is coming back, so its area is not free for someone else to redo.
        Assert.Throws<GovernanceException>(() => Add(task, "W2", area));
    }

    // Occupancy is a command-time coordination rule, not a structural invariant of the event.
    // Replay must accept every history that was ever legal, including work items recorded before
    // the rule existed. Adding this rule to the replay validator once made a real task unreadable.
    [Fact]
    public void ReplayAcceptsAHistoryWhoseWorkItemsHoldOverlappingAreas()
    {
        var taskId = new TaskId("legacy");
        var actor = new ActorId("operator");
        var reducer = new TaskReducer();
        var recordedAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var repo = Path.GetFullPath(".");

        GovernedTaskState? state = null;
        LedgerEvent Next(LedgerEventData data) => new(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{taskId.Value}:{(state?.Version ?? 0) + 1:D10}"),
            taskId, actor, recordedAt, null, "replay", data);

        state = reducer.Apply(state, Next(new TaskOpened("Legacy", "Goal")));
        state = reducer.Apply(state, Next(new RoleAssigned(new RoleAssignment(
            actor, RoleKind.Operator, Enum.GetValues<Capability>(),
            new Provenance(actor, recordedAt, "task.open")))));
        state = reducer.Apply(state, Next(new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Narrow", actor, WorkItemStatus.Proposed, [],
            [Path.Combine(repo, "src")]))));

        // The overlapping one: legal when it was written, so replay must not reject it now.
        var overlapping = Next(new WorkItemAdded(new WorkItem(
            new WorkItemId("W2"), "Everything", actor, WorkItemStatus.Proposed, [], [repo])));
        state = reducer.Apply(state, overlapping);

        Assert.Equal(2, state.WorkItems.Count);
        Assert.Equal([repo], state.WorkItems[new WorkItemId("W2")].ResourceScope);
    }

    // The same principle, caught a second time on a different rule: a run completed before the
    // "must stay resumable" rule existed carries no session identity, and replay must still
    // accept it. Enforcing that rule on the replay side made a live task unreadable.
    [Fact]
    public void ReplayAcceptsACompletedRunRecordedBeforeTheSessionIdentityRule()
    {
        var taskId = new TaskId("legacy-run");
        var actor = new ActorId("operator");
        var reducer = new TaskReducer();
        var recordedAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

        GovernedTaskState? state = null;
        LedgerEvent Next(LedgerEventData data) => new(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{taskId.Value}:{(state?.Version ?? 0) + 1:D10}"),
            taskId, actor, recordedAt, null, "replay", data);

        state = reducer.Apply(state, Next(new TaskOpened("Legacy", "Goal")));
        state = reducer.Apply(state, Next(new RoleAssigned(new RoleAssignment(
            actor, RoleKind.Operator, Enum.GetValues<Capability>(),
            new Provenance(actor, recordedAt, "task.open")))));
        state = reducer.Apply(state, Next(new RunStarted(new AgentRun(
            new RunId("R1"), actor, null, "codex", null, AgentRunStatus.Active, recordedAt, null))));

        // No session identity, and no launch token: exactly the shape recorded before either rule.
        state = reducer.Apply(state, Next(new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, null, recordedAt)));

        Assert.Equal(AgentRunStatus.Completed, state.Runs[new RunId("R1")].Status);
        Assert.Null(state.Runs[new RunId("R1")].ProviderSessionId);
    }

    private static void Add(TestTask task, string id, string scope) =>
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(),
            new WorkItemId(id), $"Work {id}", task.OperatorId, [], [scope]));
}
