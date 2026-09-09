using AILedger.Memory.Contracts;
using AILedger.Memory.Storage;
using AILedger.Memory.Tests.Support;
using Microsoft.Data.Sqlite;

namespace AILedger.Memory.Tests.Storage;

public sealed class ProjectionStoreTests
{
    [Fact]
    public async Task Q15_ProductionStoreRoundTripsTheDeclaredCheckpointType()
    {
        using var directory = new TemporaryDirectory();
        var factory = new SqliteMemoryProjectionStoreFactory(Path.Combine(directory.Path, "memory.sqlite"));
        await using (var rebuild = await factory.BeginRebuildAsync())
        {
            await using var transaction = await rebuild.Store.BeginUpdateAsync();
            await transaction.ApplyAsync(Delta(offset: 123));
            await transaction.CommitAsync();
            await rebuild.ReplaceCurrentAsync();
        }

        var store = await factory.OpenCurrentAsync();
        var checkpoint = Assert.IsType<RefusalJournalCheckpoint>(await store.GetCheckpointAsync("refusals"));
        Assert.Equal(123, checkpoint.CommittedByteOffset);
        Assert.Equal(7, checkpoint.CommittedLineCount);
        Assert.Equal("prefix-hash", checkpoint.PrefixHash);
    }

    [Fact]
    public async Task FailedSourceTransactionDoesNotAdvanceCheckpointOrExposeDocuments()
    {
        using var directory = new TemporaryDirectory();
        var factory = new SqliteMemoryProjectionStoreFactory(Path.Combine(directory.Path, "memory.sqlite"));
        await using (var rebuild = await factory.BeginRebuildAsync())
        {
            await rebuild.ReplaceCurrentAsync();
        }

        var store = await factory.OpenCurrentAsync();
        await using (var transaction = await store.BeginUpdateAsync())
        {
            await transaction.ApplyAsync(Delta(offset: 123, document: Document("temporary", "not visible")));
        }

        Assert.Null(await store.GetCheckpointAsync("refusals"));
        Assert.Null(await store.InspectAsync("temporary"));
    }

    [Fact]
    public async Task R4_FailedRebuildPreservesPreviousDatabase()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "memory.sqlite");
        var factory = new SqliteMemoryProjectionStoreFactory(databasePath);
        await using (var first = await factory.BeginRebuildAsync())
        {
            await using var transaction = await first.Store.BeginUpdateAsync();
            await transaction.ApplyAsync(Delta(offset: 123, document: Document("stable", "last good projection")));
            await transaction.CommitAsync();
            await first.ReplaceCurrentAsync();
        }

        await using (var failed = await factory.BeginRebuildAsync())
        {
            await using var transaction = await failed.Store.BeginUpdateAsync();
            await transaction.ApplyAsync(Delta(offset: 456, document: Document("replacement", "must not appear")));
            await transaction.CommitAsync();
            // Simulate ingestion/validation failure by disposing without replacement.
        }

        var current = await factory.OpenCurrentAsync();
        Assert.Equal("last good projection", (await current.InspectAsync("stable"))!.Text);
        Assert.Null(await current.InspectAsync("replacement"));
        Assert.Equal(123, Assert.IsType<RefusalJournalCheckpoint>(
            await current.GetCheckpointAsync("refusals")).CommittedByteOffset);
    }

    [Theory]
    [InlineData("{\"checkpointType\":\"future\"}")]
    [InlineData("{\"SourceId\":\"refusals\",\"Kind\":\"RefusalJournal\",\"SourceFingerprint\":\"fingerprint-123\",\"ObservedAt\":\"1970-01-01T00:00:00+00:00\",\"CommittedByteOffset\":123,\"CommittedLineCount\":7,\"PrefixHash\":\"prefix-hash\",\"checkpointType\":\"refusalJournal\"}")]
    public async Task MalformedPolymorphicCheckpointNamesSourceAndRebuildAtBothReadSites(string malformed)
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "memory.sqlite");
        var factory = new SqliteMemoryProjectionStoreFactory(databasePath);
        await using (var rebuild = await factory.BeginRebuildAsync())
        {
            await using var transaction = await rebuild.Store.BeginUpdateAsync();
            await transaction.ApplyAsync(Delta(offset: 123));
            await transaction.CommitAsync();
            await rebuild.ReplaceCurrentAsync();
        }

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE sources SET checkpoint_json = $checkpoint WHERE id = 'refusals'";
            command.Parameters.AddWithValue("$checkpoint", malformed);
            await command.ExecuteNonQueryAsync();
        }

        var store = await factory.OpenCurrentAsync();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => store.GetCheckpointAsync("refusals"));
        Assert.Contains("refusals", error.Message, StringComparison.Ordinal);
        Assert.Contains("memory projection", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rebuild", error.Message, StringComparison.OrdinalIgnoreCase);

        await using var update = await store.BeginUpdateAsync();
        var updateError = await Assert.ThrowsAsync<InvalidDataException>(() =>
            update.ApplyAsync(Delta(offset: 456, previousCheckpoint: Checkpoint(123))));
        Assert.Equal(error.Message, updateError.Message);
    }

    [Fact]
    public async Task ConcurrentRebuildRefusalPreservesActiveCandidateAndSkipsArtifactSweep()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "memory.sqlite");
        var factory = new SqliteMemoryProjectionStoreFactory(databasePath);
        await using var active = await factory.BeginRebuildAsync();
        await using (var transaction = await active.Store.BeginUpdateAsync())
        {
            await transaction.ApplyAsync(Delta(offset: 123, document: Document("active", "active candidate")));
            await transaction.CommitAsync();
        }

        var candidate = Assert.Single(Directory.EnumerateFiles(directory.Path, ".memory.sqlite.*.rebuild"));
        var candidateBytes = await File.ReadAllBytesAsync(candidate);
        var sweepSentinel = Path.Combine(directory.Path, ".memory.sqlite.stale.rebuild");
        await File.WriteAllTextAsync(sweepSentinel, "must survive refused contender");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.BeginRebuildAsync());

        Assert.Contains("already in progress", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(candidate));
        Assert.Equal(candidateBytes, await File.ReadAllBytesAsync(candidate));
        Assert.True(File.Exists(sweepSentinel));

        await active.ReplaceCurrentAsync();
        var current = await factory.OpenCurrentAsync();
        Assert.Equal("active candidate", (await current.InspectAsync("active"))!.Text);
    }

    [Fact]
    public async Task RebuildDeletesOnlyStaleSiblingArtifacts()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "memory.sqlite");
        var stale = Path.Combine(directory.Path, ".memory.sqlite.deadbeef.rebuild");
        var staleWal = stale + "-wal";
        var unrelated = Path.Combine(directory.Path, ".memory.sqlite.deadbeef.rebuild.backup");
        await File.WriteAllTextAsync(stale, "stale");
        await File.WriteAllTextAsync(staleWal, "stale");
        await File.WriteAllTextAsync(unrelated, "keep");

        await using (var rebuild = await new SqliteMemoryProjectionStoreFactory(databasePath).BeginRebuildAsync())
        {
            Assert.False(File.Exists(stale));
            Assert.False(File.Exists(staleWal));
            Assert.True(File.Exists(unrelated));
        }
    }

    [Fact]
    public async Task ReusableEmbeddingLookupHandlesMoreThanFortyThousandHashes()
    {
        using var directory = new TemporaryDirectory();
        var factory = new SqliteMemoryProjectionStoreFactory(Path.Combine(directory.Path, "memory.sqlite"));
        await using (var rebuild = await factory.BeginRebuildAsync())
        {
            await rebuild.ReplaceCurrentAsync();
        }

        var document = Document("embedded", "reusable vector");
        var identity = new EmbeddingIdentity
        {
            Provider = "deterministic", Model = "probe", Dimensions = 2, Version = "1"
        };
        var store = await factory.OpenCurrentAsync();
        await using (var transaction = await store.BeginUpdateAsync())
        {
            await transaction.ApplyAsync(Delta(offset: 123, document: document));
            await transaction.SetActiveEmbeddingIdentityAsync(identity);
            await transaction.StoreEmbeddingsAsync([
                new DocumentEmbedding
                {
                    DocumentId = document.Id,
                    ContentHash = document.ContentHash,
                    Identity = identity,
                    ChunkIndex = 0,
                    ChunkCount = 1,
                    Vector = new float[] { 1, 0 },
                    EmbeddedAt = DateTimeOffset.UnixEpoch
                }
            ]);
            await transaction.CommitAsync();
        }

        var hashes = Enumerable.Range(0, 40_001)
            .Select(index => $"missing-{index}")
            .Append(document.ContentHash)
            .ToHashSet(StringComparer.Ordinal);
        var reusable = await store.FindReusableEmbeddingsAsync(hashes, identity);

        Assert.Equal(document.Id, Assert.Single(reusable).DocumentId);
    }

    [Fact]
    public async Task NaturalLanguageLexicalQueryUsesOrRankingAndTreatsTokensLiterally()
    {
        using var directory = new TemporaryDirectory();
        var factory = new SqliteMemoryProjectionStoreFactory(Path.Combine(directory.Path, "memory.sqlite"));
        await using (var rebuild = await factory.BeginRebuildAsync())
        {
            await rebuild.ReplaceCurrentAsync();
        }

        var store = await factory.OpenCurrentAsync();
        await using (var transaction = await store.BeginUpdateAsync())
        {
            await transaction.ApplyAsync(Delta(
                offset: 123,
                documents:
                [
                    Document("writer-lock", "writer lock governs concurrent mutation"),
                    Document("unrelated", "work item completion rules")
                ]));
            await transaction.CommitAsync();
        }

        var candidates = await store.FindLexicalCandidatesAsync(
            "how does the writer \"lock\" work?",
            new MemoryCandidateFilter(),
            10);

        Assert.NotEmpty(candidates);
        Assert.Equal("writer-lock", candidates[0].Document.Id);
        Assert.True(
            candidates.Count == 1 || candidates[0].Score > candidates[1].Score,
            "The writer-lock document must outrank a weaker match rather than merely appear.");
    }

    private static SourceDelta Delta(
        long offset,
        MemoryDocument? document = null,
        IReadOnlyList<MemoryDocument>? documents = null,
        SourceCheckpoint? previousCheckpoint = null) => new()
    {
        Source = new SourceDescriptor
        {
            Id = "refusals", Kind = CanonicalSourceKind.RefusalJournal, CanonicalPath = "refusals.jsonl"
        },
        PreviousCheckpoint = previousCheckpoint,
        NextCheckpoint = Checkpoint(offset),
        Upserts = documents ?? (document is null ? [] : [document])
    };

    private static RefusalJournalCheckpoint Checkpoint(long offset) => new()
    {
        SourceId = "refusals", Kind = CanonicalSourceKind.RefusalJournal,
        SourceFingerprint = $"fingerprint-{offset}", ObservedAt = DateTimeOffset.UnixEpoch,
        CommittedByteOffset = offset, CommittedLineCount = 7, PrefixHash = "prefix-hash"
    };

    internal static MemoryDocument Document(
        string id,
        string text,
        MemoryLifecycle lifecycle = MemoryLifecycle.Active) => new()
    {
        Id = id,
        Kind = MemoryDocumentKind.Decision,
        Text = text,
        Repository = "AILedger",
        TaskId = "T1",
        SourceTimestamp = DateTimeOffset.Parse("2026-09-08T12:00:00Z"),
        SourceVersion = "1",
        Authority = MemoryAuthority.AcceptedDecision,
        Lifecycle = lifecycle,
        Citations =
        [
            new MemoryCitation
            {
                Kind = MemoryCitationKind.Event, SourcePath = "events.jsonl", LineNumber = 1, RecordId = id
            }
        ],
        ContentHash = MemoryIdentity.CreateContentHash(text),
        NormalizerVersion = MemoryIdentity.NormalizerVersion
    };
}
