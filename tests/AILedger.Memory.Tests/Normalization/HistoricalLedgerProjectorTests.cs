using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Memory.Contracts;
using AILedger.Memory.Ingestion;
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
