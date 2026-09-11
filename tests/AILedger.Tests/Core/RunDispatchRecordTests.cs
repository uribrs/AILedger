using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// K16 and D7: the limit a launch was given and the provider's own reason for a run that ended other
// than by completing both reach the run record. Without the first, a run that died at a limit the
// coordinator set too low cannot be told from one that failed on its own merits; without the second,
// nothing can tell why a dispatch ended at all.
//
// They travel the same road TruncatedLines does, and RunTruncationRecordTests beside this file is the
// same set of assertions for that field: a nullable trailing field on the completion command, on
// run.completed and on AgentRun, with no rule at command time and none at replay. MC4 with ME4 is the
// check of all three readers of the log, and R4 is why the symmetry matters — a replay rule keyed on
// a field older events lack has twice made a live task permanently unreadable here.
public sealed class RunDispatchRecordTests
{
    private const string LimitProperty = "\"launchTimeoutSeconds\":";
    private const string ReasonProperty = "\"terminalFailureReason\":";

    // The two fields reach the record at all. Without this the change is unobservable, which is what
    // measures 11 and 12 were absent for.
    [Fact]
    public void CompletionRecordsTheLimitTheLaunchWasGivenAndWhyTheRunEnded()
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var outcome = new CommandHandler().Handle(
            state,
            Complete(actor, AgentRunStatus.Cancelled, 1800, "Provider run timed out."),
            now);

        var run = outcome.State.Runs[new RunId("R1")];
        Assert.Equal(1800, run.LaunchTimeoutSeconds);
        Assert.Equal("Provider run timed out.", run.TerminalFailureReason);
    }

    // A run that completed on its own terms records a limit and no reason, which is the ordinary
    // shape and the one measure 12 must never read as waste. The pair is independent: neither field
    // implies the other, so there is no all-or-nothing rule here.
    [Fact]
    public void ACompletedRunRecordsItsLimitAndNoReason()
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var outcome = new CommandHandler().Handle(
            state, Complete(actor, AgentRunStatus.Completed, 1800, null), now);

        var run = outcome.State.Runs[new RunId("R1")];
        Assert.Equal(1800, run.LaunchTimeoutSeconds);
        Assert.Null(run.TerminalFailureReason);
    }

    // A blank reason is recorded as no reason at all. A provider that returns an empty string is
    // reporting nothing, and storing the empty string would make the record say a failure was
    // reported with no account of it — which is exactly the state the field exists to remove.
    [Fact]
    public void ABlankReasonIsRecordedAsNoReasonRatherThanAsAnEmptyOne()
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var outcome = new CommandHandler().Handle(
            state, Complete(actor, AgentRunStatus.Failed, 1800, "   "), now);

        Assert.Null(outcome.State.Runs[new RunId("R1")].TerminalFailureReason);
    }

    // R4, the guard. Every run.completed on disk carries neither field, and the shape is legal by
    // construction rather than by exemption: both are nullable and trailing, and nothing in
    // TaskTransitionValidator keys on either.
    [Fact]
    public void ReplayAcceptsARunCompletedFromBeforeTheseFieldsExisted()
    {
        var state = Replayed(out var reducer, out var next, out var actor, out var recordedAt);

        state = reducer.Apply(state, next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", recordedAt)));

        var run = state.Runs[new RunId("R1")];
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Null(run.LaunchTimeoutSeconds);
        Assert.Null(run.TerminalFailureReason);
    }

    // Replay records what the event carries, so a state rebuilt from the log is the state the handler
    // produced. Without this the two halves could disagree and only one of them would be tested.
    [Fact]
    public void ReplayRecordsTheLimitAndTheReasonTheEventCarries()
    {
        var state = Replayed(out var reducer, out var next, out var actor, out var recordedAt);

        state = reducer.Apply(state, next(state, actor, new RunCompleted(
                new RunId("R1"), AgentRunStatus.Cancelled, "session-1", recordedAt)
            with
            {
                LaunchTimeoutSeconds = 600, TerminalFailureReason = "Provider run timed out."
            }));

        var run = state.Runs[new RunId("R1")];
        Assert.Equal(600, run.LaunchTimeoutSeconds);
        Assert.Equal("Provider run timed out.", run.TerminalFailureReason);
    }

    // The third input, and the one measure 11 actually attributes on. Three states and not two: a
    // launch whose deadline expired, a run the launcher watched end some other way, and a completion
    // that observed neither. The third is what a hand-issued 'run complete' records, and it must stay
    // distinguishable from the second — "nobody looked" is not "it did not time out".
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompletionRecordsWhetherTheRunEndedAtItsLaunchTimeout(bool endedAtTheLaunchTimeout)
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var outcome = new CommandHandler().Handle(
            state,
            Complete(actor, AgentRunStatus.Cancelled, 1800, "Provider run timed out.",
                endedAtTheLaunchTimeout),
            now);

        Assert.Equal(
            endedAtTheLaunchTimeout,
            outcome.State.Runs[new RunId("R1")].EndedAtTheLaunchTimeout);
    }

    // The third state, and the one that carries the most weight: a completion that observed nothing.
    // A hand-issued 'run complete' is exactly this, and it must not read as "it did not time out".
    [Fact]
    public void ACompletionThatObservedNothingRecordsNeitherAnswer()
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var outcome = new CommandHandler().Handle(
            state,
            Complete(actor, AgentRunStatus.Cancelled, 1800, "Provider run timed out."),
            now);

        Assert.Null(outcome.State.Runs[new RunId("R1")].EndedAtTheLaunchTimeout);
    }

    // R4 again, for the third field: it is nullable and trailing like the two before it, so a
    // run.completed written before it existed replays unchanged and nothing in the validator keys on
    // it. This is the rule that has twice made a live task permanently unreadable when it was broken.
    [Fact]
    public void ReplayAcceptsARunCompletedFromBeforeTheTerminationSignalExisted()
    {
        var state = Replayed(out var reducer, out var next, out var actor, out var recordedAt);

        state = reducer.Apply(state, next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Cancelled, "session-1", recordedAt)
            with
            {
                LaunchTimeoutSeconds = 1800, TerminalFailureReason = "Provider run timed out."
            }));

        // The limit and the reason are there; what ended the run is not, because that event predates
        // the observation. A reader must not read the first two as evidence for the third.
        var run = state.Runs[new RunId("R1")];
        Assert.Equal(1800, run.LaunchTimeoutSeconds);
        Assert.Null(run.EndedAtTheLaunchTimeout);
    }

    // The root of byte-identical replay: the writer omits nulls, so a run recording neither field
    // serialises exactly as it did before they existed. The projection of an old history cannot
    // change just because the record grew.
    [Fact]
    public void ARunRecordingNeitherFieldSerialisesWithNeitherProperty()
    {
        var run = new AgentRun(
            new RunId("R1"), new ActorId("operator"), null, "claude", "session-1",
            AgentRunStatus.Completed, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.Serialize(run, LedgerJson.CreateOptions());

        Assert.DoesNotContain(LimitProperty, json, StringComparison.Ordinal);
        Assert.DoesNotContain(ReasonProperty, json, StringComparison.Ordinal);
    }

    // And the same for the event, so a run.completed written today with neither field is the same
    // bytes as one written before they existed.
    [Fact]
    public void ARunCompletedCarryingNeitherFieldSerialisesWithNeitherProperty()
    {
        var completed = new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.Serialize<LedgerEventData>(completed, LedgerJson.CreateOptions());

        Assert.DoesNotContain(LimitProperty, json, StringComparison.Ordinal);
        Assert.DoesNotContain(ReasonProperty, json, StringComparison.Ordinal);
    }

    // The success criterion end to end for the old shape: a task whose run.completed predates the two
    // fields is materialised, its state.json captured, the file deleted, the events alone replayed by
    // a fresh service, and the two files compared byte for byte. The assertions around the comparison
    // are what make it worth anything — the first shows the log holds the pre-change shape rather than
    // today's with two nulls in it, the last shows the projection carries neither property either.
    [Fact]
    public async Task ATaskWhoseRunPredatesTheseFieldsReplaysToAByteIdenticalStateFile()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("dispatch-legacy-replay");
        var actor = new ActorId("operator");
        var service = await StageCompletedRunAsync(
            root.Path, taskId, actor, AgentRunStatus.Completed, null, null);

        var line = PersistedRunCompletedLine(root.Path, taskId);
        Assert.DoesNotContain(LimitProperty, line, StringComparison.Ordinal);
        Assert.DoesNotContain(ReasonProperty, line, StringComparison.Ordinal);

        var before = await service.GetStateAsync(taskId, CancellationToken.None);
        var expected = await File.ReadAllBytesAsync(StatePath(root.Path, taskId));

        File.Delete(StatePath(root.Path, taskId));
        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.Equal(before!.Version, replayed!.Version);
        Assert.Equal(expected, await File.ReadAllBytesAsync(StatePath(root.Path, taskId)));
        var state = await File.ReadAllTextAsync(StatePath(root.Path, taskId));
        Assert.DoesNotContain(LimitProperty, state, StringComparison.Ordinal);
        Assert.DoesNotContain(ReasonProperty, state, StringComparison.Ordinal);
    }

    // The other half, and what stops the test above from passing against a dead write path: a run
    // that did record both puts them in the log and in the projection, and that history still replays
    // byte for byte.
    [Fact]
    public async Task ATaskWhoseRunRecordedBothStillReplaysByteIdentically()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("dispatch-measured-replay");
        var actor = new ActorId("operator");
        var service = await StageCompletedRunAsync(
            root.Path, taskId, actor, AgentRunStatus.Cancelled, 900, "Provider run timed out.");

        var line = PersistedRunCompletedLine(root.Path, taskId);
        Assert.Contains(LimitProperty, line, StringComparison.Ordinal);
        Assert.Contains(ReasonProperty, line, StringComparison.Ordinal);

        var before = await service.GetStateAsync(taskId, CancellationToken.None);
        var expected = await File.ReadAllBytesAsync(StatePath(root.Path, taskId));

        File.Delete(StatePath(root.Path, taskId));
        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.Equal(before!.Version, replayed!.Version);
        Assert.Equal(expected, await File.ReadAllBytesAsync(StatePath(root.Path, taskId)));
        Assert.Equal(900, replayed.Runs[new RunId("R1")].LaunchTimeoutSeconds);
        Assert.Equal("Provider run timed out.", replayed.Runs[new RunId("R1")].TerminalFailureReason);
        // And in status, which is this same state serialised with the same options, indented. A field
        // on the record that no command prints is a field nobody reads.
        var printed = JsonSerializer.Serialize(replayed, LedgerJson.CreateOptions(indented: true));
        Assert.Contains(LimitProperty, printed, StringComparison.Ordinal);
        Assert.Contains(ReasonProperty, printed, StringComparison.Ordinal);
    }

    // The causal link the third input needs, and the reason MC1 says no field was added for it: a
    // dispatch's cause is the envelope's own causationId, which every event in this kernel already
    // carries. This asserts the whole road — a command naming a cause puts it on run.started, and
    // replay reads the same id back.
    [Fact]
    public async Task ADispatchNamingItsCausePutsThatEventIdOnRunStarted()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("dispatch-causation");
        var actor = new ActorId("operator");
        var service = Service(root.Path);
        await service.ExecuteAsync(
            taskId,
            new OpenTaskCommand(actor, null, "c1", taskId, "Dispatch causation", "Goal"),
            CancellationToken.None);
        var claim = await service.ExecuteAsync(
            taskId,
            new AddClaimCommand(actor, null, "c2", new ClaimId("C1"), "The premise", null),
            CancellationToken.None);
        var cause = claim.Events[^1].EventId;

        var started = await service.ExecuteAsync(
            taskId,
            new StartRunCommand(actor, cause, "c3", new RunId("R1"), null, "claude", "session-1"),
            CancellationToken.None);

        var runStarted = Assert.Single(started.Events, @event => @event.Data is RunStarted);
        Assert.Equal(cause, runStarted.CausationId);
        // And it survives the round trip through the log, which is what a later retrospective reads.
        File.Delete(StatePath(root.Path, taskId));
        var history = new List<LedgerEvent>();
        await foreach (var @event in Service(root.Path).GetHistoryAsync(taskId, CancellationToken.None))
        {
            history.Add(@event);
        }

        Assert.Equal(cause, Assert.Single(history, @event => @event.Data is RunStarted).CausationId);
    }

    private static CompleteRunCommand Complete(
        ActorId actor,
        AgentRunStatus status,
        int? launchTimeoutSeconds,
        string? terminalFailureReason,
        bool? endedAtTheLaunchTimeout = null) =>
        new CompleteRunCommand(actor, null, "c-complete", new RunId("R1"), status, "session-1")
        {
            LaunchTimeoutSeconds = launchTimeoutSeconds,
            TerminalFailureReason = terminalFailureReason,
            EndedAtTheLaunchTimeout = endedAtTheLaunchTimeout
        };

    private static async Task<FileGovernedTaskService> StageCompletedRunAsync(
        string root,
        TaskId taskId,
        ActorId actor,
        AgentRunStatus status,
        int? launchTimeoutSeconds,
        string? terminalFailureReason)
    {
        var service = Service(root);
        await service.ExecuteAsync(
            taskId,
            new OpenTaskCommand(actor, null, "c1", taskId, "Dispatch record", "Goal"),
            CancellationToken.None);
        await service.ExecuteAsync(
            taskId,
            new StartRunCommand(actor, null, "c2", new RunId("R1"), null, "claude", "session-1"),
            CancellationToken.None);
        await service.ExecuteAsync(
            taskId,
            Complete(actor, status, launchTimeoutSeconds, terminalFailureReason) with
            {
                CorrelationId = "c3"
            },
            CancellationToken.None);
        return service;
    }

    private static string PersistedRunCompletedLine(string root, TaskId taskId) =>
        File.ReadAllLines(Path.Combine(root, taskId.Value, "events.jsonl"))
            .Single(line => line.Contains("\"run.completed\"", StringComparison.Ordinal));

    private static string StatePath(string root, TaskId taskId) =>
        Path.Combine(root, taskId.Value, "state.json");

    private static FileGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    private static GovernedTaskState Replayed(
        out TaskReducer reducer,
        out Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next,
        out ActorId actor,
        out DateTimeOffset recordedAt)
    {
        var id = new TaskId("dispatch-record");
        var localActor = new ActorId("operator");
        var localRecordedAt = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var localReducer = new TaskReducer();
        actor = localActor;
        recordedAt = localRecordedAt;
        reducer = localReducer;
        next = (current, eventActor, data) => new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{id.Value}:{(current?.Version ?? 0) + 1:D10}"),
            id, eventActor, localRecordedAt, null, "replay", data);

        var state = localReducer.Apply(null, next(null!, localActor, new TaskOpened("Dispatch record", "Goal")));
        state = localReducer.Apply(state, next(state, localActor, new RoleAssigned(new RoleAssignment(
            localActor, RoleKind.Operator, Enum.GetValues<Capability>(),
            new Provenance(localActor, localRecordedAt, "task.open")))));
        return localReducer.Apply(state, next(state, localActor, new RunStarted(new AgentRun(
            new RunId("R1"), localActor, null, "claude", "session-1", AgentRunStatus.Active,
            localRecordedAt, null, null, null, null, null, RoleKind.Operator))));
    }
}
