using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// D1: the truncated-line count reaches the run record, because a truncation nobody can see is a
// degraded stream reported as a whole one. It travels the same road the cost fields do — a nullable
// trailing field on the completion command, on run.completed, and on AgentRun — for the same reason:
// every run.completed already on disk carries none, and replay must keep reading those (R1, R4).
public sealed class RunTruncationRecordTests
{
    private const string TruncationProperty = "\"truncatedLines\":";

    // R1: the count reaches the record at all. Without this the fix is unobservable, which is what
    // rejected alternative ALT1 was rejected for.
    [Fact]
    public void CompletionRecordsHowManyLinesTheDrainCut()
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var outcome = new CommandHandler().Handle(state, Complete(actor, 7), now);

        Assert.Equal(7, outcome.State.Runs[new RunId("R1")].TruncatedLines);
    }

    // The distinction the field exists for, and the one MillisecondsToFirstLedgerWrite failed to
    // keep. Zero means the stream was watched and nothing was cut. Null means nobody counted — the
    // run predates the field, or died before its process produced an exit to read a count from.
    [Fact]
    public void AWatchedStreamRecordsZeroAndAnUnwatchedOneRecordsNull()
    {
        var state = Replayed(out _, out _, out var actor, out var now);
        var handler = new CommandHandler();

        var watched = handler.Handle(state, Complete(actor, 0), now)
            .State.Runs[new RunId("R1")].TruncatedLines;
        var unwatched = handler.Handle(state, Complete(actor, null), now)
            .State.Runs[new RunId("R1")].TruncatedLines;

        Assert.Equal(0, watched);
        Assert.Null(unwatched);
    }

    // R4, the guard. Every run.completed on disk carries no count, and a replay rule keyed on a
    // field older events lack has twice made a live task permanently unreadable in this repository.
    // The field is nullable and trailing, so this history is legal by construction rather than by
    // exemption — and nothing in TaskTransitionValidator keys on it.
    [Fact]
    public void ReplayAcceptsARunCompletedFromBeforeTheTruncationCountExisted()
    {
        var state = Replayed(out var reducer, out var next, out var actor, out var recordedAt);

        state = reducer.Apply(state, next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", recordedAt)));

        var run = state.Runs[new RunId("R1")];
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Null(run.TruncatedLines);
    }

    // Replay records the same number the command path does, so a state rebuilt from the log is the
    // state the handler produced.
    [Fact]
    public void ReplayRecordsATruncationCountTheEventCarries()
    {
        var state = Replayed(out var reducer, out var next, out var actor, out var recordedAt);

        state = reducer.Apply(state, next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", recordedAt) with { TruncatedLines = 2 }));

        Assert.Equal(2, state.Runs[new RunId("R1")].TruncatedLines);
    }

    // The root of byte-identical replay: the ledger writer omits nulls, so a run that counted
    // nothing serialises exactly as it did before the field existed. The projection of an old
    // history cannot change just because the record grew.
    [Fact]
    public void ARunThatCountedNothingSerialisesWithNoTruncationProperty()
    {
        var run = new AgentRun(
            new RunId("R1"), new ActorId("operator"), null, "claude", "session-1",
            AgentRunStatus.Completed, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.Serialize(run, LedgerJson.CreateOptions());

        Assert.DoesNotContain(TruncationProperty, json, StringComparison.Ordinal);
    }

    // And the same for the event, so a run.completed written today with no count is the same bytes
    // as one written before the field existed.
    [Fact]
    public void ARunCompletedCarryingNoCountSerialisesWithNoTruncationProperty()
    {
        var completed = new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.Serialize<LedgerEventData>(completed, LedgerJson.CreateOptions());

        Assert.DoesNotContain(TruncationProperty, json, StringComparison.Ordinal);
    }

    // The success criterion end to end: a task whose run.completed predates the count is
    // materialised, its state.json captured, the file deleted, the events alone replayed by a fresh
    // service, and the two files compared byte for byte. The two assertions around the comparison are
    // what make it worth anything — the first shows the log holds the pre-count shape rather than
    // today's with a null in it, the last shows the projection carries no such property either.
    [Fact]
    public async Task ATaskWhoseRunPredatesTheTruncationCountReplaysToAByteIdenticalStateFile()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("truncation-legacy-replay");
        var actor = new ActorId("operator");
        var service = await StageCompletedRunAsync(root.Path, taskId, actor, truncatedLines: null);

        Assert.DoesNotContain(
            TruncationProperty, PersistedRunCompletedLine(root.Path, taskId), StringComparison.Ordinal);

        var before = await service.GetStateAsync(taskId, CancellationToken.None);
        var expected = await File.ReadAllBytesAsync(StatePath(root.Path, taskId));

        File.Delete(StatePath(root.Path, taskId));
        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.Equal(before!.Version, replayed!.Version);
        Assert.Equal(expected, await File.ReadAllBytesAsync(StatePath(root.Path, taskId)));
        Assert.DoesNotContain(
            TruncationProperty,
            await File.ReadAllTextAsync(StatePath(root.Path, taskId)),
            StringComparison.Ordinal);
    }

    // The other half, and what stops the test above from passing against a dead write path: a run
    // that did report a count puts the property in the log and in the projection, and that history
    // still replays byte for byte.
    [Fact]
    public async Task ATaskWhoseRunReportedACountWritesItAndStillReplaysByteIdentically()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("truncation-measured-replay");
        var actor = new ActorId("operator");
        var service = await StageCompletedRunAsync(root.Path, taskId, actor, truncatedLines: 4);

        Assert.Contains(
            TruncationProperty, PersistedRunCompletedLine(root.Path, taskId), StringComparison.Ordinal);

        var before = await service.GetStateAsync(taskId, CancellationToken.None);
        var expected = await File.ReadAllBytesAsync(StatePath(root.Path, taskId));
        Assert.Contains(
            TruncationProperty,
            await File.ReadAllTextAsync(StatePath(root.Path, taskId)),
            StringComparison.Ordinal);

        File.Delete(StatePath(root.Path, taskId));
        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.Equal(before!.Version, replayed!.Version);
        Assert.Equal(expected, await File.ReadAllBytesAsync(StatePath(root.Path, taskId)));
        Assert.Equal(4, replayed.Runs[new RunId("R1")].TruncatedLines);
        // And in status, which is this same state serialised with the same options, indented. A
        // count on the record that no command prints is a count nobody reads.
        Assert.Contains(
            TruncationProperty,
            JsonSerializer.Serialize(replayed, LedgerJson.CreateOptions(indented: true)),
            StringComparison.Ordinal);
    }

    private static CompleteRunCommand Complete(ActorId actor, int? truncatedLines) =>
        new CompleteRunCommand(
            actor, null, "c-complete", new RunId("R1"), AgentRunStatus.Completed, "session-1")
        {
            TruncatedLines = truncatedLines
        };

    private static async Task<FileGovernedTaskService> StageCompletedRunAsync(
        string root,
        TaskId taskId,
        ActorId actor,
        int? truncatedLines)
    {
        var service = Service(root);
        await service.ExecuteAsync(
            taskId,
            new OpenTaskCommand(actor, null, "c1", taskId, "Overlong line", "Goal"),
            CancellationToken.None);
        await service.ExecuteAsync(
            taskId,
            new StartRunCommand(
                actor, null, "c2", new RunId("R1"), null, "claude", "session-1", "claude-opus-5-requested"),
            CancellationToken.None);
        await service.ExecuteAsync(
            taskId,
            Complete(actor, truncatedLines) with { CorrelationId = "c3" },
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
        var id = new TaskId("overlong-line");
        var localActor = new ActorId("operator");
        var localRecordedAt = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var localReducer = new TaskReducer();
        actor = localActor;
        recordedAt = localRecordedAt;
        reducer = localReducer;
        next = (current, eventActor, data) => new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{id.Value}:{(current?.Version ?? 0) + 1:D10}"),
            id, eventActor, localRecordedAt, null, "replay", data);

        var state = localReducer.Apply(null, next(null!, localActor, new TaskOpened("Overlong line", "Goal")));
        state = localReducer.Apply(state, next(state, localActor, new RoleAssigned(new RoleAssignment(
            localActor, RoleKind.Operator, Enum.GetValues<Capability>(),
            new Provenance(localActor, localRecordedAt, "task.open")))));
        return localReducer.Apply(state, next(state, localActor, new RunStarted(new AgentRun(
            new RunId("R1"), localActor, null, "claude", "session-1", AgentRunStatus.Active,
            localRecordedAt, null, "claude-opus-5-requested", null, null, null, RoleKind.Operator))));
    }
}
