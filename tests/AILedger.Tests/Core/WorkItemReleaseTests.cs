using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// A work item that turned out to be the wrong split had only two exits: finish it, or leave it
// holding its directory area for the rest of the task. Neither is honest, and the second blocks
// every better split of the same code. Abandoning is the third exit: the operator says why, the
// item stops pretending to be live, and the area goes back.
public sealed class WorkItemReleaseTests
{
    [Fact]
    public void AbandoningAWorkItemReleasesItsAreaForAnOverlappingOne()
    {
        var task = new TestTask();
        var area = Path.GetFullPath("src/AILedger.Core");
        Add(task, "W1", area);

        task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "  The split was wrong  "));

        var abandoned = task.State.WorkItems[new WorkItemId("W1")];
        Assert.Equal(WorkItemStatus.Abandoned, abandoned.Status);
        // The reason is stored the way every other governed reason is: trimmed, and kept.
        Assert.Equal("The split was wrong", abandoned.AbandonReason);

        Add(task, "W2", area);
        Assert.Equal(WorkItemStatus.Proposed, task.State.WorkItems[new WorkItemId("W2")].Status);
    }

    [Fact]
    public void OnlyAnOperatorCanAbandonGovernedWork()
    {
        var task = new TestTask();
        Add(task, "W1", Path.GetFullPath("src"));
        var lead = new ActorId("lead");
        // The lead holds ManageWork and may complete work items, so the refusal is about who it is.
        // Deciding that governed work will not be done is the operator's call, like defining it.
        task.Assign(lead, RoleKind.ImplementationLead, Capability.ManageWork);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new AbandonWorkItemCommand(
            lead, null, task.NextCorrelation(), new WorkItemId("W1"), "I would rather not do this")));

        Assert.Contains("Only an operator can abandon governed work.", error.Message, StringComparison.Ordinal);
        Assert.Equal(WorkItemStatus.Proposed, task.State.WorkItems[new WorkItemId("W1")].Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AbandoningWithoutAReasonIsRefused(string reason)
    {
        var task = new TestTask();
        Add(task, "W1", Path.GetFullPath("src"));

        // Releasing an area silently is how the next planner inherits a decision with no argument
        // behind it. The reason is the whole point of the command.
        Assert.Throws<GovernanceException>(() => task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), reason)));
        Assert.Equal(WorkItemStatus.Proposed, task.State.WorkItems[new WorkItemId("W1")].Status);
    }

    [Fact]
    public void ACompletedWorkItemCannotBeAbandoned()
    {
        var task = new TestTask();
        Add(task, "W1", Path.GetFullPath("src"));
        task.RecordRequiredRuns(new WorkItemId("W1"));
        task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1")));

        // Work that was done stays done. Abandoning it would erase a completion from the record
        // rather than add a decision to it.
        Assert.Throws<GovernanceException>(() => task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "On second thought")));
        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[new WorkItemId("W1")].Status);
    }

    [Fact]
    public void AnAbandonedWorkItemCannotBeAbandonedASecondTime()
    {
        var task = new TestTask();
        Add(task, "W1", Path.GetFullPath("src"));
        task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "The split was wrong"));

        // The second reason would overwrite the first, and the first is the one that was acted on.
        Assert.Throws<GovernanceException>(() => task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "A different reason")));
        Assert.Equal("The split was wrong", task.State.WorkItems[new WorkItemId("W1")].AbandonReason);
    }

    [Fact]
    public void AbandoningIsRefusedWhileARunOnTheItemIsStillActive()
    {
        var task = new TestTask();
        Add(task, "W1", Path.GetFullPath("src"));
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(),
            new RunId("R1"), new WorkItemId("W1"), "codex", null));

        // An agent is working inside that area right now. Releasing it would hand the same files to
        // a second work item while the first is still writing to them.
        Assert.Throws<GovernanceException>(() => task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "The split was wrong")));
        Assert.NotEqual(WorkItemStatus.Abandoned, task.State.WorkItems[new WorkItemId("W1")].Status);

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Cancelled, null));
        task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "The split was wrong"));

        Assert.Equal(WorkItemStatus.Abandoned, task.State.WorkItems[new WorkItemId("W1")].Status);
    }

    // An abandoned item is not live work. Everything that treats it as live has to refuse, or the
    // release is only cosmetic: the area is handed to a second work item while agents are still
    // being pointed at the first.
    [Fact]
    public void ARunCannotBeStartedOnAnAbandonedWorkItem()
    {
        var task = new TestTask();
        Add(task, "W1", Path.GetFullPath("src"));
        task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "The split was wrong"));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), new WorkItemId("W1"), "codex", null)));

        Assert.Contains("Cannot start a run for work item in status 'Abandoned'", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(new RunId("R1"), task.State.Runs.Keys);
    }

    [Fact]
    public void AnAbandonedWorkItemCannotBeCompleted()
    {
        var task = new TestTask();
        Add(task, "W1", Path.GetFullPath("src"));
        // Both runs completion needs are staged before the abandonment, so the completion below
        // cannot be refused for a missing run. Being abandoned is the only thing left to refuse it.
        task.RecordRequiredRuns(new WorkItemId("W1"));
        task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "The split was wrong"));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"))));
        // The refusal points at the way forward, because there is one: a new work item.
        Assert.Contains("Add a new work item instead of reviving this one", error.Message, StringComparison.Ordinal);
        // Nor by waiving: a waiver skips the verifier, not the work item's own status.
        Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "It was nearly done")));
        Assert.Equal(WorkItemStatus.Abandoned, task.State.WorkItems[new WorkItemId("W1")].Status);
    }

    [Fact]
    public void AnAbandonedWorkItemCannotBeBlocked()
    {
        var task = new TestTask();
        Add(task, "W1", Path.GetFullPath("src"));
        task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "The split was wrong"));

        // Blocking says "this is coming back". Nothing is coming back, and a Blocked item would
        // take its area with it.
        var error = Assert.Throws<GovernanceException>(() => task.Apply(new BlockWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Waiting on the operator", null)));

        Assert.Contains("cannot be blocked", error.Message, StringComparison.Ordinal);
        Assert.Equal(WorkItemStatus.Abandoned, task.State.WorkItems[new WorkItemId("W1")].Status);
    }

    // The second copy of the three rules above. A forged log that treats an abandoned item as live
    // has to be refused at replay too, or a tampered ledger reads back as a legal one.
    [Fact]
    public void ReplayRefusesEveryTransitionThatTreatsAnAbandonedItemAsLive()
    {
        var state = Abandoned(out var reducer, out var next, out var actor, out var recordedAt);

        var started = next(state, actor, new RunStarted(new AgentRun(
            new RunId("R1"), actor, new WorkItemId("W1"), "codex", null, AgentRunStatus.Active, recordedAt, null)));
        var completed = next(state, actor, new WorkItemCompleted(new WorkItemId("W1")));
        var blocked = next(state, actor, new WorkItemBlocked(
            new WorkItemId("W1"), "Waiting on the operator", null));

        Assert.Throws<GovernanceException>(() => reducer.Apply(state, started));
        Assert.Throws<GovernanceException>(() => reducer.Apply(state, completed));
        Assert.Throws<GovernanceException>(() => reducer.Apply(state, blocked));
    }

    // Abandoning is refused while a run is active, so no command sequence reaches this. It is
    // reachable by hand, and a log written by an older kernel could hold exactly this shape: the
    // reducer must not let a closing run quietly resurrect a released item as Paused, because a
    // Paused item holds its area and the area has already been given away.
    [Fact]
    public void AClosingRunDoesNotResurrectAnAbandonedWorkItem()
    {
        var state = Opened("abandoned-run-close", out var reducer, out var next, out var actor, out var recordedAt);
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Wrong split", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));
        state = reducer.Apply(state, next(state, actor, new RunStarted(new AgentRun(
            new RunId("R1"), actor, new WorkItemId("W1"), "codex", null, AgentRunStatus.Active, recordedAt, null))));
        state = reducer.Apply(state, next(state, actor, new WorkItemAbandoned(
            new WorkItemId("W1"), "The split was wrong")));

        state = reducer.Apply(state, next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", recordedAt)));

        Assert.Equal(AgentRunStatus.Completed, state.Runs[new RunId("R1")].Status);
        Assert.Equal(WorkItemStatus.Abandoned, state.WorkItems[new WorkItemId("W1")].Status);
    }

    // Refinement repoints live dependents onto the sharper claim. An abandoned item is not live,
    // and its record of what it was actually built on is the only thing it still has to say.
    [Fact]
    public void ARefinementDoesNotRepointAnAbandonedWorkItem()
    {
        var task = new TestTask();
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(),
            new ClaimId("C1"), "The API is stable", null));
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(),
            new ClaimId("C2"), "The API is stable below 200 rps", null));
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E2"),
            "probe", "cite", "supports C2", [new ClaimId("C2")], []));
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"),
            "Wrong split", task.OperatorId, [new ClaimId("C1")], [Path.GetFullPath("src")]));
        task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "The split was wrong"));
        // C2 is validated and nothing refutes C1, so superseding earns the refinement path.
        task.Apply(new ResolveClaimCommand(task.OperatorId, null, task.NextCorrelation(),
            new ClaimId("C2"), ClaimStatus.Validated, [new EvidenceId("E2")]));

        task.Apply(new ResolveClaimCommand(task.OperatorId, null, task.NextCorrelation(),
            new ClaimId("C1"), ClaimStatus.Superseded, [], new ClaimId("C2")));

        var workItem = task.State.WorkItems[new WorkItemId("W1")];
        Assert.Equal([new ClaimId("C1")], workItem.DependsOnClaims);
        Assert.Equal(WorkItemStatus.Abandoned, workItem.Status);
    }

    // Supporting a work challenge blocks the item, and an abandoned item cannot be blocked. The
    // refusal has to happen here, at command time, with a message about the consequence — not as an
    // incidental explosion inside the block rules, and never as an event replay would then reject.
    [Fact]
    public void AChallengeAgainstAnAbandonedWorkItemCannotBeSupported()
    {
        var task = new TestTask();
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(),
            new ClaimId("C1"), "The API is stable", null));
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E1"),
            "probe", "cite", "refutes C1", [], [new ClaimId("C1")]));
        Add(task, "W1", Path.GetFullPath("src"));
        task.Apply(new RaiseChallengeCommand(task.OperatorId, null, task.NextCorrelation(),
            new ChallengeId("CH1"), "work", "W1", "It does not hold", [new EvidenceId("E1")]));
        task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "The split was wrong"));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new DisposeChallengeCommand(
            task.OperatorId, null, task.NextCorrelation(), new ChallengeId("CH1"), ChallengeStatus.Supported)));

        // The message is the whole point: this is refused where the challenge is disposed, in terms
        // of the challenge, and not by the block rule exploding one layer further down.
        Assert.Contains("supporting this challenge would change nothing", error.Message, StringComparison.Ordinal);
        Assert.Equal(ChallengeStatus.Open, task.State.Challenges[new ChallengeId("CH1")].Status);
        Assert.Equal(WorkItemStatus.Abandoned, task.State.WorkItems[new WorkItemId("W1")].Status);
    }

    // An invalidation moves an abandoned item on to Stale, and Stale used to be a way back in:
    // blocking a stale item is how a released area got taken back while a second work item already
    // held it. Stale is terminal here for the same reason Abandoned and Completed are.
    [Fact]
    public void AStaleWorkItemCannotBeBlockedBackIntoLife()
    {
        var task = Invalidated(out var area);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new BlockWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Reopening it", null)));

        Assert.Contains("cannot be blocked", error.Message, StringComparison.Ordinal);
        Assert.Equal(WorkItemStatus.Stale, task.State.WorkItems[new WorkItemId("W1")].Status);
        // The area is still the second item's alone: nothing became live again.
        Assert.Contains("already held by work item 'W2'",
            Assert.Throws<GovernanceException>(() => Add(task, "W3", area)).Message, StringComparison.Ordinal);
    }

    // The abandon path back in, and the reason it matters beyond occupancy: a second abandonment
    // would overwrite the reason the operator actually acted on with a later, different one.
    [Fact]
    public void AbandoningAgainAfterAnInvalidationCannotOverwriteTheOriginalReason()
    {
        var task = Invalidated(out _);

        Assert.Throws<GovernanceException>(() => task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "A different reason")));

        Assert.Equal("The split was wrong", task.State.WorkItems[new WorkItemId("W1")].AbandonReason);
        Assert.Equal(WorkItemStatus.Stale, task.State.WorkItems[new WorkItemId("W1")].Status);
    }

    // Refusing Stale in BlockWorkItem is command-time only, and this is the other half of that
    // decision. Unlike abandonment, blocking is old: a log written before this rule could hold a
    // stale item that was blocked, and it was legal when written. Replay has to keep reading it.
    [Fact]
    public void ReplayAcceptsAStaleWorkItemThatWasBlockedBeforeTheRuleExisted()
    {
        var task = Invalidated(out _);
        var reducer = new TaskReducer();
        var blocked = new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{task.TaskId.Value}:{task.State.Version + 1:D10}"),
            task.TaskId, task.OperatorId, new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero),
            null, "replay", new WorkItemBlocked(new WorkItemId("W1"), "Blocked long ago", null));

        var state = reducer.Apply(task.State, blocked);

        Assert.Equal(WorkItemStatus.Blocked, state.WorkItems[new WorkItemId("W1")].Status);
    }

    // W1 is abandoned, its area is taken by W2, and then the claim W1 depended on is rejected —
    // which moves W1 from Abandoned to Stale. That is the state both rules above have to hold.
    private static TestTask Invalidated(out string area)
    {
        var task = new TestTask();
        area = Path.GetFullPath("src/AILedger.Core");
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(),
            new ClaimId("C1"), "The API is stable", null));
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"),
            "Wrong split", task.OperatorId, [new ClaimId("C1")], [area]));
        task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "The split was wrong"));
        Add(task, "W2", area);
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E1"),
            "probe", "cite", "refutes C1", [], [new ClaimId("C1")]));
        task.Apply(new ResolveClaimCommand(task.OperatorId, null, task.NextCorrelation(),
            new ClaimId("C1"), ClaimStatus.Rejected, [new EvidenceId("E1")]));
        Assert.Equal(WorkItemStatus.Stale, task.State.WorkItems[new WorkItemId("W1")].Status);
        return task;
    }

    [Fact]
    public void ReplayAcceptsAnAbandonedWorkItemAndKeepsItsReason()
    {
        var state = Opened("abandon-replay", out var reducer, out var next, out var actor, out _);
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Wrong split", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));

        state = reducer.Apply(state, next(state, actor, new WorkItemAbandoned(
            new WorkItemId("W1"), "The split was wrong")));

        Assert.Equal(WorkItemStatus.Abandoned, state.WorkItems[new WorkItemId("W1")].Status);
        Assert.Equal("The split was wrong", state.WorkItems[new WorkItemId("W1")].AbandonReason);
    }

    // The second copy of the rule. Operator-only is a structural property of the event, not a
    // coordination rule, so a forged log claiming a lead abandoned governed work is refused at
    // replay as well as at command time.
    [Fact]
    public void ReplayRefusesAnAbandonmentByANonOperator()
    {
        var state = Opened("abandon-authority-replay", out var reducer, out var next, out var actor, out var recordedAt);
        var lead = new ActorId("lead");
        state = reducer.Apply(state, next(state, actor, new RoleAssigned(new RoleAssignment(
            lead, RoleKind.ImplementationLead, [Capability.ManageWork],
            new Provenance(actor, recordedAt, "actor.assign-role")))));
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Wrong split", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));

        var forged = next(state, lead, new WorkItemAbandoned(new WorkItemId("W1"), "The split was wrong"));

        var error = Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
        Assert.Contains("Only an operator", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayRefusesAnAbandonmentWithNoReason()
    {
        var state = Opened("abandon-reason-replay", out var reducer, out var next, out var actor, out _);
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Wrong split", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));

        // A reason-less abandonment is not a legal history that predates the rule; the event type
        // was born with the reason on it, so replay may refuse this without rejecting anyone's past.
        var forged = next(state, actor, new WorkItemAbandoned(new WorkItemId("W1"), "   "));

        Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
    }

    // A replayed history that has already released W1's area, ready for a forged event to be tried
    // against it.
    private static GovernedTaskState Abandoned(
        out TaskReducer reducer,
        out Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next,
        out ActorId actor,
        out DateTimeOffset recordedAt)
    {
        var state = Opened("abandoned-replay", out reducer, out next, out actor, out recordedAt);
        var localNext = next;
        var localActor = actor;
        state = reducer.Apply(state, localNext(state, localActor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Wrong split", localActor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));
        return reducer.Apply(state, localNext(state, localActor, new WorkItemAbandoned(
            new WorkItemId("W1"), "The split was wrong")));
    }

    private static GovernedTaskState Opened(
        string taskId,
        out TaskReducer reducer,
        out Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next,
        out ActorId actor,
        out DateTimeOffset recordedAt)
    {
        var id = new TaskId(taskId);
        var localActor = new ActorId("operator");
        var localRecordedAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var localReducer = new TaskReducer();
        actor = localActor;
        recordedAt = localRecordedAt;
        reducer = localReducer;
        next = (current, eventActor, data) => new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{id.Value}:{(current?.Version ?? 0) + 1:D10}"),
            id, eventActor, localRecordedAt, null, "replay", data);

        var state = localReducer.Apply(null, next(null!, localActor, new TaskOpened("Legacy", "Goal")));
        return localReducer.Apply(state, next(state, localActor, new RoleAssigned(new RoleAssignment(
            localActor, RoleKind.Operator, Enum.GetValues<Capability>(),
            new Provenance(localActor, localRecordedAt, "task.open")))));
    }

    private static void Add(TestTask task, string id, string scope) =>
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(),
            new WorkItemId(id), $"Work {id}", task.OperatorId, [], [scope]));
}
