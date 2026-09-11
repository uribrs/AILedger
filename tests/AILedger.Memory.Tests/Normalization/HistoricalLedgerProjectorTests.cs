using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Memory.Contracts;
using AILedger.Core.Domain;
using AILedger.Memory.Ingestion;
using AILedger.Memory.Normalization;
using AILedger.Memory.Tests.Support;
using AILedger.Storage;

namespace AILedger.Memory.Tests.Normalization;

// HistoricalLedgerProjector is a third replay of the event log beside CommandHandler and
// TaskTransitionValidator, and its switch throws on event data it has no arm for. That is not a
// hypothetical: when the context gate shipped, neither of its two events had an arm, so the index
// could not rebuild any task that had ever been briefed — which by then was nearly all of them, and
// nothing surfaced it because nothing reads this index yet.
//
// EventHistoryIncrementalTests pins that pair. This class pins the coordinator session's pair, which
// is attention item R3 on the orchestration plan for the same work item that added them.
public sealed class HistoricalLedgerProjectorTests
{
    // R3 (third-reader-event-break): a history carrying session.started and session.completed is
    // projected rather than refused, and the run that names the session still projects with it. The
    // assertion is deliberately on the rebuilt documents and not on an absence of exceptions: a
    // reader that swallowed the event would pass a "did not throw" test while indexing nothing.
    [Fact]
    public async Task R3_session_events_rebuild()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var sessionId = new CoordinatorSessionId("S1");
        var workId = new WorkItemId("W1");
        var started = DateTimeOffset.UnixEpoch.AddSeconds(2);
        await WriteEventsAsync(path, [
            Event(1, new TaskOpened("Fixture", "Replay a bracketed task")),
            Event(2, new SessionStarted(new CoordinatorSession(
                sessionId, new ActorId("operator"), "claude-code", "harness-1", started, null))),
            Event(3, new WorkItemAdded(new WorkItem(
                workId, "Work dispatched inside a coordinator session", null,
                WorkItemStatus.Paused, [], ["tests"]))),
            Event(4, new RunStarted(new AgentRun(
                new RunId("R1"), new ActorId("worker"), workId, "claude", "provider-session",
                AgentRunStatus.Active, DateTimeOffset.UnixEpoch.AddSeconds(4), null,
                CoordinatorSessionId: sessionId))),
            Event(5, new RunCompleted(
                new RunId("R1"), AgentRunStatus.Completed, "provider-session",
                DateTimeOffset.UnixEpoch.AddSeconds(5))),
            Event(6, new SessionCompleted(sessionId, DateTimeOffset.UnixEpoch.AddSeconds(6)))
        ], append: false);

        var delta = await new EventHistorySourceReader().ReadAsync(Source(path));

        var work = Assert.Single(delta.Upserts, item => item.Kind == MemoryDocumentKind.WorkSummary);
        Assert.Contains("Work dispatched inside a coordinator session", work.Text, StringComparison.Ordinal);
    }

    // The other half of the same rule, and the half that is easy to leave untested: a task recorded
    // before sessions existed must project exactly as it did before. Sessions are optional for the
    // whole existing history, and all 7,019 events in this repository were written without one.
    [Fact]
    public async Task AHistoryWithNoSessionStillRebuilds()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var workId = new WorkItemId("W1");
        await WriteEventsAsync(path, [
            Event(1, new TaskOpened("Fixture", "Replay an unbracketed task")),
            Event(2, new WorkItemAdded(new WorkItem(
                workId, "Work with no coordinator session", null, WorkItemStatus.Paused, [], ["tests"])))
        ], append: false);

        var delta = await new EventHistorySourceReader().ReadAsync(Source(path));

        var work = Assert.Single(delta.Upserts, item => item.Kind == MemoryDocumentKind.WorkSummary);
        Assert.Contains("Work with no coordinator session", work.Text, StringComparison.Ordinal);
    }

    // The defect this projector actually had, seen from the production reader. A run completed with a
    // served model that differs from the requested one must rebuild saying which cognition ran; the
    // projector was not copying Model at all, so the document said the model that was asked for.
    //
    // Model is the assertion here because it is the one field of the ten this reader had fallen behind
    // on that a rebuilt document renders (LedgerDocumentNormalizer.NormalizeRun). The other nine are
    // pinned by ItsRunCompletionAgreesWithTheCanonicalReducer below, which compares the projections
    // themselves rather than what a document happens to show.
    [Fact]
    public async Task ARunCompletedThroughTheProductionReaderCarriesTheServedModel()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        await WriteEventsAsync(path, [
            Event(1, new TaskOpened("Fixture", "Replay a completed run")),
            Event(2, new RunStarted(new AgentRun(
                new RunId("R1"), new ActorId("worker"), null, "claude", "provider-session",
                AgentRunStatus.Active, DateTimeOffset.UnixEpoch.AddSeconds(2), null,
                Model: "the-model-that-was-requested"))),
            Event(3, new RunCompleted(
                new RunId("R1"), AgentRunStatus.Completed, "provider-session",
                DateTimeOffset.UnixEpoch.AddSeconds(3),
                Model: "the-model-that-actually-ran"))
        ], append: false);

        var delta = await new EventHistorySourceReader().ReadAsync(Source(path));

        var run = Assert.Single(delta.Upserts, item => item.Kind == MemoryDocumentKind.RunSummary);
        Assert.Contains("the-model-that-actually-ran", run.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("the-model-that-was-requested", run.Text, StringComparison.Ordinal);
    }

    // The anti-drift test, and the reason it asserts on whole records rather than on a field list:
    // this reader fell ten fields behind the canonical one without a single test failing, because
    // every one of those fields was added to an arm that already existed. A per-field assertion would
    // have had to be extended by the same person who forgot to extend the projection. Record equality
    // cannot be forgotten — a field added to RunCompleted and projected by only one of the two readers
    // fails here on the next run of the suite.
    //
    // The payload deliberately carries a non-default value in every field RunCompleted has, including
    // the launch limit and the provider's terminal reason that the code review named (UC1, UE5).
    [Fact]
    public void ItsRunCompletionAgreesWithTheCanonicalReducer()
    {
        var completed = new RunCompleted(
            new RunId("R1"), AgentRunStatus.Cancelled, "provider-session",
            CompletedAt,
            ManifestHash: new string('a', 64),
            ManifestArtifactCount: 7,
            Turns: 11,
            OutputTokens: 2_200_000_000L,
            MillisecondsToFirstLedgerWrite: 4321L,
            Model: "the-model-that-actually-ran",
            TokensInUncached: 3_100_000_000L,
            TokensInCacheWrite: 3_200_000_000L,
            TokensInCacheRead: 3_300_000_000L,
            TruncatedLines: 5,
            LaunchTimeoutSeconds: 1800,
            TerminalFailureReason: "Provider run timed out.",
            EndedAtTheLaunchTimeout: true);

        var projected = Replay(HistoricalLedgerProjector.Apply, completed);
        var canonical = Replay(new TaskReducer().Apply, completed);

        Assert.Equal(canonical.Runs[new RunId("R1")], projected.Runs[new RunId("R1")]);
        // Named separately so a failure says which contract broke rather than only that two records
        // differ. These are the two the review found dropped.
        Assert.Equal(1800, projected.Runs[new RunId("R1")].LaunchTimeoutSeconds);
        Assert.Equal("Provider run timed out.", projected.Runs[new RunId("R1")].TerminalFailureReason);
    }

    // The one sequence number the run is started at, and the one it is completed at. Fixed rather than
    // counted, because the canonical reducer checks that a started run begins active at its own
    // event's timestamp and that a role's provenance matches its own event's — so the payloads and
    // their envelopes have to be composed from the same number.
    private const long StartSequence = 3;
    private const long CompleteSequence = 4;

    private static DateTimeOffset CompletedAt => DateTimeOffset.UnixEpoch.AddSeconds(CompleteSequence);

    // Both readers over exactly the same events. The task, the opening role and the started run are
    // the least history the stricter of the two will accept; the canonical reducer validates authority
    // and shape as it replays, while the memory projector deliberately does not, because it indexes
    // histories that may predate their own role assignments. Only a history the strict reader accepts
    // can be put through both, which is what makes the comparison possible at all.
    private static GovernedTaskState Replay(
        Func<GovernedTaskState?, LedgerEvent, GovernedTaskState> reader,
        RunCompleted completed)
    {
        var operatorId = new ActorId("operator");
        GovernedTaskState? state = reader(null, Event(1, new TaskOpened("Fixture", "Replay one run")));
        state = reader(state, Event(2, new RoleAssigned(new RoleAssignment(
            operatorId,
            RoleKind.Operator,
            Enum.GetValues<Capability>(),
            new Provenance(
                operatorId,
                DateTimeOffset.UnixEpoch.AddSeconds(2),
                // The opening role, which the kernel writes as part of task.open rather than as a
                // separate assignment, so that is the source it validates against.
                "task.open")))));
        state = reader(state, Event(StartSequence, new RunStarted(new AgentRun(
            new RunId("R1"), operatorId, null, "claude", null,
            AgentRunStatus.Active, DateTimeOffset.UnixEpoch.AddSeconds(StartSequence), null,
            Model: "the-model-that-was-requested"))));
        return reader(state, Event(CompleteSequence, completed));
    }

    private static async Task WriteEventsAsync(
        string path,
        IEnumerable<LedgerEvent> events,
        bool append)
    {
        var rows = string.Join('\n', events.Select(item =>
            JsonSerializer.Serialize(item, LedgerJson.CreateOptions()))) + "\n";
        if (append)
        {
            await File.AppendAllTextAsync(path, rows);
        }
        else
        {
            await File.WriteAllTextAsync(path, rows);
        }
    }

    private static SourceDescriptor Source(string path) => new()
    {
        Id = "events",
        Kind = CanonicalSourceKind.EventHistory,
        CanonicalPath = path,
        TaskId = "T1"
    };

    private static LedgerEvent Event(long sequence, LedgerEventData data) => new(
        1,
        new EventId($"EV{sequence}"),
        new TaskId("T1"),
        new ActorId("operator"),
        DateTimeOffset.UnixEpoch.AddSeconds(sequence),
        null,
        $"correlation-{sequence}",
        data);
}
