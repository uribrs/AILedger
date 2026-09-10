using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// The coordinator is modelled as a session, not as a run, and the session owns the runs it
// dispatches (D1). These tests are about that bracket: what opens it, what closes it, what a
// dispatched run may point at — and above all what stays legal when no session exists at all,
// because every one of the 7,019 events in this repository was written without one.
public sealed class CoordinatorSessionTests
{
    [Fact]
    public void ASessionBracketsItsActorsWorkAndOwnsTheRunsItDispatches()
    {
        var task = new TestTask();
        var session = new CoordinatorSessionId("S1");
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.AddClaim, Capability.BuildContext);

        task.Apply(new StartCoordinatorSessionCommand(
            task.OperatorId, null, task.NextCorrelation(), session, "claude-code", "harness-1"));
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), null, "claude", null,
            null, null, null, worker, null, null, null, session));
        task.Apply(new CompleteCoordinatorSessionCommand(
            task.OperatorId, null, task.NextCorrelation(), session));

        var bracket = task.State.CoordinatorSessions[session];
        Assert.Equal(task.OperatorId, bracket.ActorId);
        Assert.Equal("claude-code", bracket.Harness);
        Assert.Equal("harness-1", bracket.HarnessSessionId);
        Assert.NotNull(bracket.EndedAt);
        Assert.True(bracket.EndedAt >= bracket.StartedAt);
        // The parent link, which is the whole point: the run points at the bracket rather than the
        // bracket listing its children, so a run recorded outside one carries null and needs no
        // entry anywhere.
        Assert.Equal(session, task.State.Runs[new RunId("R1")].CoordinatorSessionId);
    }

    // Two overlapping brackets on one actor would put the same dispatched run inside both, and the
    // idle time inside a session — the measure that matters most here — would then be computed
    // against a span that is not the conversation's.
    [Fact]
    public void OneActorCannotHoldTwoOpenSessionsAtOnce()
    {
        var task = new TestTask();
        task.Apply(new StartCoordinatorSessionCommand(
            task.OperatorId, null, task.NextCorrelation(), new CoordinatorSessionId("S1"),
            "claude-code", "harness-1"));

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(
            new StartCoordinatorSessionCommand(
                task.OperatorId, null, task.NextCorrelation(), new CoordinatorSessionId("S2"),
                "claude-code", "harness-2")));

        Assert.Contains("already has an open coordinator session", refusal.Message, StringComparison.Ordinal);
    }

    // Closing the first one hands the actor its next bracket, which is the ordinary shape of a day's
    // work: several conversations in sequence, each measured on its own.
    [Fact]
    public void ClosingASessionLetsTheSameActorOpenTheNextOne()
    {
        var task = new TestTask();
        var first = new CoordinatorSessionId("S1");
        task.Apply(new StartCoordinatorSessionCommand(
            task.OperatorId, null, task.NextCorrelation(), first, "claude-code", "harness-1"));
        task.Apply(new CompleteCoordinatorSessionCommand(
            task.OperatorId, null, task.NextCorrelation(), first));

        task.Apply(new StartCoordinatorSessionCommand(
            task.OperatorId, null, task.NextCorrelation(), new CoordinatorSessionId("S2"),
            "claude-code", "harness-2"));

        Assert.Equal(2, task.State.CoordinatorSessions.Count);
    }

    [Fact]
    public void ASessionCannotBeClosedTwice()
    {
        var task = new TestTask();
        var session = new CoordinatorSessionId("S1");
        task.Apply(new StartCoordinatorSessionCommand(
            task.OperatorId, null, task.NextCorrelation(), session, "claude-code", null));
        task.Apply(new CompleteCoordinatorSessionCommand(
            task.OperatorId, null, task.NextCorrelation(), session));

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(
            new CompleteCoordinatorSessionCommand(
                task.OperatorId, null, task.NextCorrelation(), session)));

        Assert.Contains("already been closed", refusal.Message, StringComparison.Ordinal);
    }

    // A lead coordinates its own bracket, and an operator may close one that was left open. A third
    // actor may not, on the same grounds run completion uses.
    [Fact]
    public void OnlyTheSessionActorOrAnOperatorCanCloseASession()
    {
        var task = new TestTask();
        var lead = new ActorId("planning-lead");
        var otherLead = new ActorId("other-lead");
        task.Assign(lead, RoleKind.PlanningLead, Capability.ManageRuns, Capability.BuildContext);
        task.Assign(otherLead, RoleKind.ImplementationLead, Capability.ManageRuns, Capability.BuildContext);
        var session = new CoordinatorSessionId("S1");
        task.Apply(new StartCoordinatorSessionCommand(
            lead, null, task.NextCorrelation(), session, "codex-cli", null));

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(
            new CompleteCoordinatorSessionCommand(otherLead, null, task.NextCorrelation(), session)));
        task.Apply(new CompleteCoordinatorSessionCommand(
            task.OperatorId, null, task.NextCorrelation(), session));

        Assert.Contains("Only session actor 'planning-lead' or an operator", refusal.Message, StringComparison.Ordinal);
        Assert.NotNull(task.State.CoordinatorSessions[session].EndedAt);
    }

    // The same capability dispatching a run needs, and deliberately not a new one: a session is the
    // bracket around dispatching, so the seats that may dispatch are the seats that may open one.
    [Fact]
    public void AnActorWithNoRunAuthorityCannotOpenASession()
    {
        var task = new TestTask();
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.AddClaim, Capability.BuildContext);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(
            new StartCoordinatorSessionCommand(
                worker, null, task.NextCorrelation(), new CoordinatorSessionId("S1"), "claude-code", null)));

        Assert.Contains("lacks capability 'ManageRuns'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASessionRequiresAnIdAndAHarness()
    {
        var task = new TestTask();

        var missingId = Assert.Throws<GovernanceException>(() => task.Apply(
            new StartCoordinatorSessionCommand(
                task.OperatorId, null, task.NextCorrelation(), new CoordinatorSessionId(" "),
                "claude-code", null)));
        var missingHarness = Assert.Throws<GovernanceException>(() => task.Apply(
            new StartCoordinatorSessionCommand(
                task.OperatorId, null, task.NextCorrelation(), new CoordinatorSessionId("S1"), " ", null)));

        Assert.Contains("SessionId is required", missingId.Message, StringComparison.Ordinal);
        // A session that does not say what it ran in cannot have its usage record looked for, and
        // "no record" and "never asked" are different absences (D6).
        Assert.Contains("Harness is required", missingHarness.Message, StringComparison.Ordinal);
    }

    // The link is checked only when a run names one. This is the rule that must never tighten: a run
    // naming no session is every run this ledger has ever recorded.
    [Fact]
    public void ARunThatNamesNoSessionIsStillAccepted()
    {
        var task = new TestTask();
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.AddClaim, Capability.BuildContext);

        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), null, "claude", null,
            null, null, null, worker));

        Assert.Null(task.State.Runs[new RunId("R1")].CoordinatorSessionId);
        Assert.Empty(task.State.CoordinatorSessions);
    }

    [Fact]
    public void ARunCannotNameASessionThatDoesNotExist()
    {
        var task = new TestTask();
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.AddClaim, Capability.BuildContext);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), null, "claude", null,
            null, null, null, worker, null, null, null, new CoordinatorSessionId("S404"))));

        Assert.Contains("Unknown coordinator session 'S404'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AClosedSessionCannotDispatchFurtherRuns()
    {
        var task = new TestTask();
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.AddClaim, Capability.BuildContext);
        var session = new CoordinatorSessionId("S1");
        task.Apply(new StartCoordinatorSessionCommand(
            task.OperatorId, null, task.NextCorrelation(), session, "claude-code", null));
        task.Apply(new CompleteCoordinatorSessionCommand(
            task.OperatorId, null, task.NextCorrelation(), session));

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), null, "claude", null,
            null, null, null, worker, null, null, null, session)));

        Assert.Contains("is closed and cannot dispatch further runs", refusal.Message, StringComparison.Ordinal);
    }

    // Letting one actor's run claim another's session would attribute the dispatch — and the idle
    // time around it — to a conversation that never made it.
    [Fact]
    public void OneActorCannotDispatchInsideAnothersSession()
    {
        var task = new TestTask();
        var lead = new ActorId("planning-lead");
        task.Assign(lead, RoleKind.PlanningLead, Capability.ManageRuns, Capability.BuildContext);
        var session = new CoordinatorSessionId("S1");
        task.Apply(new StartCoordinatorSessionCommand(
            lead, null, task.NextCorrelation(), session, "codex-cli", null));

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), null, "claude", null,
            null, null, null, null, null, null, null, session)));

        Assert.Contains("belongs to 'planning-lead'", refusal.Message, StringComparison.Ordinal);
    }

    // Replay is the second reader and it must accept what command time produced. This records the
    // whole history through the handler and then reduces it from nothing, which is what a task
    // directory does on every read.
    [Fact]
    public void TheTwoSessionEventsReplayThroughAFreshReducer()
    {
        var handler = new CommandHandler();
        var operatorId = new ActorId("operator");
        var taskId = new TaskId("session-replay");
        var session = new CoordinatorSessionId("S1");
        var history = new List<LedgerEvent>();
        GovernedTaskState? state = null;
        var clock = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

        foreach (var command in new LedgerCommand[]
        {
            new OpenTaskCommand(operatorId, null, "c1", taskId, "Session replay", "Bracket the loop"),
            new StartCoordinatorSessionCommand(operatorId, null, "c2", session, "claude-code", "harness-1"),
            new CompleteCoordinatorSessionCommand(operatorId, null, "c3", session)
        })
        {
            var outcome = handler.Handle(state, command, clock);
            clock = clock.AddMinutes(5);
            state = outcome.State;
            history.AddRange(outcome.Events);
        }

        var reducer = new TaskReducer();
        GovernedTaskState? replayed = null;
        foreach (var @event in history)
        {
            replayed = reducer.Apply(replayed, @event);
        }

        Assert.NotNull(replayed);
        Assert.Equal(state!.CoordinatorSessions[session], replayed!.CoordinatorSessions[session]);
        Assert.Equal(state.Version, replayed.Version);
    }

    // The forged shapes replay refuses. Each one is safe to refuse here by construction rather than
    // by exemption: no session.started or session.completed already on disk carries any of them,
    // because neither event type existed until now.
    [Fact]
    public void ReplayRefusesASessionThatDoesNotBelongToItsOwnEvent()
    {
        var task = new TestTask();
        var reducer = new TaskReducer();
        var forged = new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{task.TaskId.Value}:0000000099"),
            task.TaskId,
            task.OperatorId,
            new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero),
            null,
            "correlation-forged",
            new SessionStarted(new CoordinatorSession(
                new CoordinatorSessionId("S1"), new ActorId("someone-else"), "claude-code", null,
                new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero), null)));

        var refusal = Assert.Throws<GovernanceException>(() => reducer.Apply(task.State, forged));

        Assert.Contains("must belong to the event actor", refusal.Message, StringComparison.Ordinal);
    }
}
