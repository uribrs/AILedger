using System.Text.Json;
using AILedger.Memory.Contracts;

namespace AILedger.Memory.Tests.Contracts;

public sealed class FrozenContractTests
{
    [Fact]
    public void DocumentIdentityAndContentHashAreStableAndDomainSeparated()
    {
        var firstId = MemoryIdentity.CreateDocumentId("source", MemoryDocumentKind.Decision, "D1");
        var secondId = MemoryIdentity.CreateDocumentId("source", MemoryDocumentKind.Decision, "D1");

        Assert.Equal(firstId, secondId);
        Assert.Equal(64, firstId.Length);
        Assert.NotEqual(firstId, MemoryIdentity.CreateDocumentId("source", MemoryDocumentKind.Decision, "D2"));
        Assert.NotEqual(firstId, MemoryIdentity.CreateDocumentId("source", MemoryDocumentKind.Constraint, "D1"));
        Assert.NotEqual(firstId, MemoryIdentity.CreateContentHash("sourceDecisionD1"));
        Assert.Equal(
            MemoryIdentity.CreateContentHash("normalized text"),
            MemoryIdentity.CreateContentHash("normalized text"));
    }

    [Fact]
    public void CheckpointHierarchyRoundTripsAllSourceSpecificState()
    {
        SourceCheckpoint[] checkpoints =
        [
            new EventHistoryCheckpoint
            {
                SourceId = "events", Kind = CanonicalSourceKind.EventHistory,
                SourceFingerprint = "event-fingerprint", ObservedAt = DateTimeOffset.UnixEpoch,
                FileLength = 100, FileContentHash = "event-hash", EventCount = 3, LastEventId = "E3"
            },
            new RefusalJournalCheckpoint
            {
                SourceId = "refusals", Kind = CanonicalSourceKind.RefusalJournal,
                SourceFingerprint = "refusal-fingerprint", ObservedAt = DateTimeOffset.UnixEpoch,
                CommittedByteOffset = 123, CommittedLineCount = 7, PrefixHash = "prefix-hash"
            },
            new LessonStoreCheckpoint
            {
                SourceId = "lessons", Kind = CanonicalSourceKind.PublishedLessons,
                SourceFingerprint = "lesson-fingerprint", ObservedAt = DateTimeOffset.UnixEpoch,
                FileLength = 200, FileContentHash = "lesson-hash", LessonCount = 5, LastLessonId = "L5"
            }
        ];

        foreach (var checkpoint in checkpoints)
        {
            var json = JsonSerializer.Serialize(checkpoint);
            Assert.StartsWith("{\"checkpointType\":", json, StringComparison.Ordinal);
            Assert.Contains($"\"Kind\":\"{checkpoint.Kind}\"", json, StringComparison.Ordinal);

            var restored = JsonSerializer.Deserialize<SourceCheckpoint>(json);
            Assert.Equal(checkpoint, restored);
        }

        var refusal = Assert.IsType<RefusalJournalCheckpoint>(checkpoints[1]);
        var delta = new SourceDelta
        {
            Source = new SourceDescriptor
            {
                Id = "refusals", Kind = CanonicalSourceKind.RefusalJournal, CanonicalPath = "refusals.jsonl"
            },
            NextCheckpoint = refusal,
            Upserts = []
        };
        var restoredDelta = JsonSerializer.Deserialize<SourceDelta>(JsonSerializer.Serialize(delta));
        var restoredRefusal = Assert.IsType<RefusalJournalCheckpoint>(restoredDelta!.NextCheckpoint);
        Assert.Equal(123, restoredRefusal.CommittedByteOffset);
        Assert.Equal(7, restoredRefusal.CommittedLineCount);
        Assert.Equal("prefix-hash", restoredRefusal.PrefixHash);
    }

    [Fact]
    public void UnknownCheckpointTypeFailsClosed()
    {
        const string json = """
            {"checkpointType":"futureSource","SourceId":"future","Kind":"EventHistory","SourceFingerprint":"fp","ObservedAt":"1970-01-01T00:00:00+00:00"}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SourceCheckpoint>(json));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void EmbeddingDimensionsOutsideFrozenBoundsAreRejected(int dimensions)
    {
        var identity = new EmbeddingIdentity
        {
            Provider = "deterministic", Model = "test", Dimensions = dimensions, Version = "1"
        };

        Assert.Throws<ArgumentOutOfRangeException>(identity.Validate);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public void EmbeddingDimensionsAtFrozenBoundsAreAccepted(int dimensions)
    {
        new EmbeddingIdentity
        {
            Provider = "deterministic", Model = "test", Dimensions = dimensions, Version = "1"
        }.Validate();
    }

    [Fact]
    public void SemanticScoreUsesTheBestChunk()
    {
        Assert.Equal(0.91f, SemanticScoring.MaximumChunkScore(new[] { -0.2f, 0.91f, 0.4f }));
        Assert.Throws<ArgumentException>(() => SemanticScoring.MaximumChunkScore(Array.Empty<float>()));
        Assert.Throws<ArgumentException>(() => SemanticScoring.MaximumChunkScore(new[] { float.NaN }));
    }

    [Fact]
    public void EmbeddingRequestDistinguishesDocumentFromQuery()
    {
        var document = new EmbeddingRequest { InputKind = EmbeddingInputKind.Document, Inputs = ["same text"] };
        var query = new EmbeddingRequest { InputKind = EmbeddingInputKind.Query, Inputs = ["same text"] };

        Assert.NotEqual(document.InputKind, query.InputKind);
    }
}
