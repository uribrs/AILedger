using System.Text;
using System.Text.Json;
using AILedger.Core.Alternatives;
using AILedger.Core.Contracts;
using AILedger.Core.Findings;
using AILedger.Tests.Findings;

namespace AILedger.Tests.Alternatives;

[Collection(AlternativesRecordingCollection.Name)]
public sealed class AlternativesOperationTests
{
    [Fact]
    public async Task AtomicCommitPreservesTextProvenanceLinksAndRetryReceiptAfterRestart()
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        await f.ExecuteAsync(new ProposeDecisionCommand(f.Actor, null, "decision", new("D1"), "Chosen",
            "Reason", [], null));
        var source = AlternativesFixture.Request();
        var request = source with { Alternatives = [source.Alternatives[0] with { ReplacedByDecisionId = "D1" }, source.Alternatives[1]] };
        var first = await f.RecordAsync(request);
        Assert.Null(first.Error);
        var receipt = first.Receipt!;
        Assert.Equal(5, receipt.LedgerVersion);
        var state = await f.StateAsync();
        for (var i = 0; i < request.Alternatives.Count; i++)
        {
            var map = receipt.Alternatives[i];
            Assert.Matches("^AF_[0-9a-f]{32}$", map.AlternativeId);
            var actual = state.Alternatives[new(map.AlternativeId)];
            Assert.Equal(request.Alternatives[i].Statement.Trim(), actual.Statement);
            Assert.Equal(request.Alternatives[i].RejectionRationale.Trim(), actual.RejectionRationale);
            Assert.Equal(request.Alternatives[i].ReplacedByDecisionId, actual.ReplacedByDecisionId?.Value);
            Assert.Equal(f.Actor, actual.Provenance.ActorId);
            Assert.Equal(receipt.CommittedAt, actual.Provenance.RecordedAt);
            Assert.Equal("alternative.record", actual.Provenance.Source);
        }
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var retry = await f.RecordAsync(request); // fixture creates a new service each time
        Assert.True(retry.Replayed);
        Assert.NotEqual(first.AttemptId, retry.AttemptId);
        Assert.Equal(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(retry.Receipt));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
        Assert.Equal(DecisionStatus.Proposed, state.Decisions[new("D1")].Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateInvalidReferenceCommitsNoPrefixAndJournalsOneRefusal(bool lesson)
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        var old = await File.ReadAllBytesAsync(f.EventsPath);
        var request = new AlternativesRequest(1, "bad", [new("first", "Valid", "Reason"),
            new("late", "Invalid", "Reason", lesson ? null : "missing", lesson ? "unrecalled" : null)]);
        var result = await f.RecordAsync(request);
        Assert.Equal("kernel_refused", result.Error?.Code);
        Assert.Equal("alternatives[1]", result.Error?.ItemPath);
        Assert.Equal("not_committed", result.Error?.CommitState);
        Assert.Equal(old, await File.ReadAllBytesAsync(f.EventsPath));
        var refusals = await File.ReadAllLinesAsync(AILedger.Storage.RefusalJournal.ResolvePath(f.Directory));
        Assert.Single(refusals);
        Assert.Contains("RecordAlternativeCommand", refusals[0]);
    }

    [Fact]
    public async Task ConcurrentIdsAndRetriesAreIndependentOfFindingsKeys()
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => f.RecordAsync(AlternativesFixture.Single("key-" + i))));
        Assert.All(results, result => Assert.Null(result.Error));
        Assert.Equal(12, results.Select(x => x.Receipt!.Alternatives[0].AlternativeId).Distinct().Count());
        var retry = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.RecordAsync(AlternativesFixture.Single("key-0"))));
        Assert.All(retry, x => Assert.True(x.Replayed));
        var findings = await f.Service().RecordAsync(new(f.TaskId, f.Actor, null, "stable",
            AllowRecordFindings: true, AllowRunless: true), FindingsFixture.Claims("key-0"), default);
        Assert.Null(findings.Error);
        Assert.Equal(12, (await f.StateAsync()).Alternatives.Count);
        Assert.Single((await f.StateAsync()).Claims);
    }

    [Fact]
    public async Task ChangedContentOrAttributionConflictsAndCapacityDoesNotRevokeReceipt()
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        var request = AlternativesFixture.Single();
        await f.RecordAsync(request);
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var changed = request with { Alternatives = [request.Alternatives[0] with { Statement = "Approach " }] };
        Assert.Equal("idempotency_conflict", (await f.RecordAsync(changed)).Error?.Code);
        Assert.Equal("committed", (await f.RecordAsync(request, f.Binding with { CorrelationId = "other" })).Error?.CommitState);
        Assert.True((await f.Service(cap: 2, bytes: 1).RecordAlternativesAsync(f.Binding, request, default)).Replayed);
        Assert.Equal("capacity_exceeded", (await f.Service(cap: 2).RecordAlternativesAsync(f.Binding,
            AlternativesFixture.Single("new"), default)).Error?.Code);
        Assert.Equal("capacity_exceeded", (await f.Service(bytes: before.Length).RecordAlternativesAsync(f.Binding,
            AlternativesFixture.Single("new"), default)).Error?.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }

    [Fact]
    public void BoundedTypedShapeAndCanonicalizationAreExplicit()
    {
        var valid = AlternativesFixture.Single();
        foreach (var bad in new[] { valid with { SchemaVersion = 2 }, valid with { RequestId = "bad key" },
                     valid with { Alternatives = [] }, valid with { Alternatives = Enumerable.Repeat(valid.Alternatives[0], 33).ToArray() },
                     valid with { Alternatives = [valid.Alternatives[0], valid.Alternatives[0]] },
                     valid with { Alternatives = [new("a", " ", "Reason")] },
                     valid with { Alternatives = [new("a", "Text", "\ud800")] },
                     valid with { Alternatives = [new("a", new string('x', 8193), "Reason")] },
                     valid with { Alternatives = [new("a", "Text", "Reason", new string('x', 257))] },
                     valid with { Alternatives = Enumerable.Range(0,32).Select(i => new AlternativeInput("a" + i, new string('x',8192), new string('y',8192))).ToArray() } })
            Assert.Throws<FindingsRequestException>(() => AlternativesValidation.Snapshot(bad));
        var binding = new AlternativesBinding(new("task"), new("author"), null, "stable", AllowRecordAlternatives: true, AllowRunless: true);
        var bytes = Encoding.UTF8.GetString(AlternativesFingerprint.CanonicalBytes(binding, valid));
        Assert.Equal("""{"operation":"record_alternatives","schema_version":1,"binding":{"task_id":"task","actor_id":"author","run_id":null,"correlation_id":"stable","causation_id":null},"alternatives":[{"key":"a","statement":"Approach","rejection_rationale":"Not suitable","replaced_by_decision_id":null,"from_lesson":null}]}""", bytes);
        Assert.Equal(AlternativesFingerprint.Compute(binding, valid), AlternativesFingerprint.Compute(binding, valid with { RequestId = "different" }));
        var items = new[] { valid.Alternatives[0] };
        var snapshot = AlternativesValidation.Snapshot(valid with { Alternatives = items });
        items[0] = new("a", "Changed", "Changed");
        Assert.Equal("Approach", snapshot.Alternatives[0].Statement);
    }
}
