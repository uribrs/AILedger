using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Memory.Contracts;
using AILedger.Memory.Ingestion;
using AILedger.Memory.Tests.Support;
using AILedger.Storage;

namespace AILedger.Memory.Tests.Ingestion;

public sealed class EventHistoryIncrementalTests
{
    [Fact]
    public async Task WorkItemInvalidationPreservesExistingBlockReason()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var workId = new WorkItemId("W1");
        await WriteEventsAsync(path, [
            Event(1, new TaskOpened("Fixture", "Preserve block reason")),
            Event(2, new WorkItemAdded(new WorkItem(
                workId, "Blocked work", null, WorkItemStatus.Paused, [], ["tests"]))),
            Event(3, new WorkItemBlocked(workId, "waiting for dependency", null)),
            Event(4, new WorkItemInvalidated(workId, new ClaimId("C1"), WorkItemStatus.Stale))
        ], append: false);

        var delta = await new EventHistorySourceReader().ReadAsync(Source(path));
        var work = Assert.Single(delta.Upserts, item => item.Kind == MemoryDocumentKind.WorkSummary);

        Assert.Equal(MemoryLifecycle.Superseded, work.Lifecycle);
        Assert.Contains("Block reason: waiting for dependency", work.Text, StringComparison.Ordinal);
    }

    // The projector is a third replay of the event log beside CommandHandler and
    // TaskTransitionValidator, and its switch throws on event data it has no arm for. When the
    // context gate shipped, neither of its two events had one, so the index could not rebuild any
    // task that had ever been briefed — which by then was nearly all of them, and nothing surfaced
    // it because nothing reads this index yet. Both events are audit records that change no state
    // the index projects, so both are no-ops; this pins that they are read rather than refused.
    [Fact]
    public async Task TheContextGatesTwoEventsAreProjectedRatherThanRefused()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var workId = new WorkItemId("W1");
        await WriteEventsAsync(path, [
            Event(1, new TaskOpened("Fixture", "Replay a briefed task")),
            Event(2, new ContextBuilt(
                RoleKind.Operator,
                null,
                [new ContextSkill("task-orchestrator", "0000000000000000000000000000000000000000000000000000000000000000")])),
            Event(3, new ContextBriefWaived("work.add", "the brief was not read", null)),
            Event(4, new WorkItemAdded(new WorkItem(
                workId, "Work added through the operator door", null, WorkItemStatus.Paused, [], ["tests"])))
        ], append: false);

        var delta = await new EventHistorySourceReader().ReadAsync(Source(path));

        var work = Assert.Single(delta.Upserts, item => item.Kind == MemoryDocumentKind.WorkSummary);
        Assert.Contains("Work added through the operator door", work.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingHistoricalReferenceNamesCanonicalSourceLineKindAndId()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        await WriteEventsAsync(path, [
            Event(1, new TaskOpened("Fixture", "Purposeful diagnostics")),
            Event(2, new WorkItemInvalidated(
                new WorkItemId("W404"), new ClaimId("C1"), WorkItemStatus.Stale))
        ], append: false);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EventHistorySourceReader().ReadAsync(Source(path)));

        Assert.Contains(Path.GetFullPath(path), error.Message, StringComparison.Ordinal);
        Assert.Contains("line 2", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("work item", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("W404", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StructurallyAbsentOrNullEventBodiesNameSourceLineAndEventTypeAndPreserveCause()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var opened = Serialize(Event(1, new TaskOpened("Fixture", "Malformed bodies")));

        var absent = JsonNode.Parse(Serialize(Event(2, new ClaimAdded(null!))))!.AsObject();
        absent["data"]!.AsObject().Remove("claim");
        await AssertMalformedBodyAsync(absent, "ClaimAdded");

        var explicitNull = JsonNode.Parse(Serialize(Event(2, new WorkItemAdded(null!))))!.AsObject();
        explicitNull["data"]!["workItem"] = null;
        await AssertMalformedBodyAsync(explicitNull, "WorkItemAdded");

        async Task AssertMalformedBodyAsync(JsonObject malformed, string eventType)
        {
            await File.WriteAllTextAsync(path, $"{opened}\n{malformed.ToJsonString()}\n");

            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new EventHistorySourceReader().ReadAsync(Source(path)));

            Assert.Contains(Path.GetFullPath(path), error.Message, StringComparison.Ordinal);
            Assert.Contains("line 2", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(eventType, error.Message, StringComparison.Ordinal);
            Assert.IsType<NullReferenceException>(error.InnerException);
        }
    }

    [Fact]
    public async Task IncompleteTypedBodiesAtReplayAndDocumentConstructionNameTheirCausalRecords()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var claim = new Claim(
            new ClaimId("C1"), "Incomplete claim", ClaimStatus.Open, [], null, null!);

        await WriteEventsAsync(path, [
            Event(1, new TaskOpened("Fixture", "Malformed typed bodies")),
            Event(2, new ClaimAdded(claim))
        ], append: false);

        var constructionError = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EventHistorySourceReader().ReadAsync(Source(path)));
        AssertMalformedTypedBody(
            constructionError, path, 2, "ClaimAdded", "claim", "C1", typeof(NullReferenceException));

        claim = claim with
        {
            EvidenceIds = [],
            Provenance = new Provenance(
                new ActorId("operator"), DateTimeOffset.UnixEpoch.AddSeconds(2), "claim.add")
        };
        await WriteEventsAsync(path, [
            Event(1, new TaskOpened("Fixture", "Malformed typed bodies")),
            Event(2, new ClaimAdded(claim)),
            Event(3, new ClaimResolved(
                claim.Id, ClaimStatus.Validated, null!, SupersededByClaimId: null))
        ], append: false);

        var replayError = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EventHistorySourceReader().ReadAsync(Source(path)));
        AssertMalformedTypedBody(
            replayError, path, 3, "ClaimResolved", "claim", "C1", typeof(ArgumentNullException));
    }

    [Fact]
    public async Task MalformedLaterEvidenceNamesItsOwnCausalRowThroughProductionReader()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var actor = new ActorId("operator");
        var claim = new Claim(
            new ClaimId("C1"), "Sound claim", ClaimStatus.Open, [], null,
            new Provenance(actor, DateTimeOffset.UnixEpoch.AddSeconds(2), "claim.add"));
        var malformedEvidence = new Evidence(
            new EvidenceId("E1"), "test-run", "probe:1", "Incomplete evidence",
            null!, [claim.Id],
            new Provenance(actor, DateTimeOffset.UnixEpoch.AddSeconds(3), "evidence.add"));
        await WriteEventsAsync(path, [
            Event(1, new TaskOpened("Fixture", "Causal evidence diagnostics")),
            Event(2, new ClaimAdded(claim)),
            Event(3, new EvidenceAdded(malformedEvidence))
        ], append: false);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EventHistorySourceReader().ReadAsync(Source(path)));

        AssertMalformedTypedBody(
            error, path, 3, "EvidenceAdded", "evidence", "E1", typeof(ArgumentNullException));
    }

    [Fact]
    public async Task MalformedClaimEvidenceIdsNamesClaimRowBeforeSoundEvidence()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var actor = new ActorId("operator");
        var claim = new Claim(
            new ClaimId("C1"), "Incomplete claim", ClaimStatus.Open, null!, null,
            new Provenance(actor, DateTimeOffset.UnixEpoch.AddSeconds(2), "claim.add"));
        var evidence = new Evidence(
            new EvidenceId("E1"), "test-run", "probe:1", "Sound evidence",
            [claim.Id], [],
            new Provenance(actor, DateTimeOffset.UnixEpoch.AddSeconds(3), "evidence.add"));
        await WriteEventsAsync(path, [
            Event(1, new TaskOpened("Fixture", "Causal claim diagnostics")),
            Event(2, new ClaimAdded(claim)),
            Event(3, new EvidenceAdded(evidence))
        ], append: false);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EventHistorySourceReader().ReadAsync(Source(path)));

        AssertMalformedTypedBody(
            error, path, 2, "ClaimAdded", "claim", "C1", typeof(ArgumentNullException));
    }

    [Fact]
    public async Task CancellationIsNotConvertedIntoMalformedBodyDiagnostic()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        await File.WriteAllTextAsync(path,
            $"{Serialize(Event(1, new TaskOpened("Fixture", "Cancellation")))}\n" +
            $"{Serialize(Event(2, new ClaimAdded(null!)))}\n");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EventHistorySourceReader().ReadAsync(Source(path), cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task RoleLessLegacyHistoryAppendReemitsSupersededArtifactAndOpenClaimEvidenceLikeRebuild()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var source = new SourceDescriptor
        {
            Id = "events",
            Kind = CanonicalSourceKind.EventHistory,
            CanonicalPath = path,
            TaskId = "T1"
        };
        var actor = new ActorId("operator");
        var handler = new CommandHandler();
        GovernedTaskState? state = null;
        var initial = new List<LedgerEvent>();
        Apply(new OpenTaskCommand(
            actor, null, "open", new TaskId("T1"), "Fixture", "Incremental normalization"),
            initial);
        Apply(new RecordArtifactCommand(
            actor, null, "artifact-1", new ArtifactId("A1"), GovernedArtifactKind.UserRequest,
            "Original", "original plan", null, null, null), initial);
        Apply(new AddClaimCommand(
            actor, null, "claim", new ClaimId("C1"), "An open claim",
            "Evidence must still be visible"), initial);
        Assert.DoesNotContain(initial, item => item.Data is RoleAssigned);
        await WriteEventsAsync(path, initial, append: false);
        var reader = new EventHistorySourceReader();
        var first = await reader.ReadAsync(source);

        var appended = new List<LedgerEvent>();
        Apply(new AddEvidenceCommand(
            actor, null, "evidence", new EvidenceId("E1"), "test-run", "probe:1",
            "Open-claim evidence", [new ClaimId("C1")], []), appended);
        Apply(new RecordArtifactCommand(
            actor, null, "artifact-2", new ArtifactId("A2"), GovernedArtifactKind.UserRequest,
            "Revision", "revised plan", null, null, new ArtifactId("A1")), appended);
        await WriteEventsAsync(path, appended, append: true);

        var incremental = await reader.ReadAsync(source, first.NextCheckpoint);
        var rebuilt = await reader.ReadAsync(source);
        var originalId = MemoryIdentity.CreateDocumentId("events", MemoryDocumentKind.Artifact, "A1");
        var revisionId = MemoryIdentity.CreateDocumentId("events", MemoryDocumentKind.Artifact, "A2");
        var claimId = MemoryIdentity.CreateDocumentId("events", MemoryDocumentKind.ClaimEvidence, "C1");

        Assert.Equal(MemoryLifecycle.Superseded,
            Assert.Single(incremental.Upserts, item => item.Id == originalId).Lifecycle);
        Assert.Equal(MemoryLifecycle.Active,
            Assert.Single(incremental.Upserts, item => item.Id == revisionId).Lifecycle);
        var claim = Assert.Single(incremental.Upserts, item => item.Id == claimId);
        Assert.Equal(MemoryLifecycle.Unresolved, claim.Lifecycle);
        Assert.Contains("Open-claim evidence", claim.Text, StringComparison.Ordinal);
        Assert.Contains("E1", claim.RelatedIds);
        Assert.Contains(claim.Citations, citation => citation.LineNumber == 4 && citation.RecordId == "C1");

        foreach (var changed in incremental.Upserts)
        {
            Assert.Equal(
                JsonSerializer.Serialize(changed),
                JsonSerializer.Serialize(Assert.Single(rebuilt.Upserts, item => item.Id == changed.Id)));
        }

        void Apply(LedgerCommand command, ICollection<LedgerEvent> target)
        {
            var outcome = handler.Handle(state, command, DateTimeOffset.Parse("2026-09-08T12:00:00Z"));
            state = outcome.State;
            foreach (var @event in outcome.Events)
            {
                if (@event.Data is not RoleAssigned)
                {
                    target.Add(@event);
                }
            }
        }
    }

    private static async Task WriteEventsAsync(
        string path,
        IEnumerable<LedgerEvent> events,
        bool append)
    {
        var rows = string.Join('\n', events.Select(item => JsonSerializer.Serialize(item, LedgerJson.CreateOptions()))) + "\n";
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

    private static string Serialize(LedgerEvent @event) =>
        JsonSerializer.Serialize(@event, LedgerJson.CreateOptions());

    private static void AssertMalformedTypedBody(
        InvalidDataException error,
        string path,
        long line,
        string eventType,
        string recordKind,
        string recordId,
        Type innerExceptionType)
    {
        Assert.Contains(Path.GetFullPath(path), error.Message, StringComparison.Ordinal);
        Assert.Contains($"line {line}", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(eventType, error.Message, StringComparison.Ordinal);
        Assert.Contains(recordKind, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(recordId, error.Message, StringComparison.Ordinal);
        Assert.IsType(innerExceptionType, error.InnerException);
    }
}
