using AILedger.Memory.Contracts;
using AILedger.Memory.Retrieval;
using AILedger.Memory.Storage;
using AILedger.Memory.Tests.Storage;
using AILedger.Memory.Tests.Support;

namespace AILedger.Memory.Tests.Retrieval;

public sealed class HybridMemorySearcherTests
{
    [Fact]
    public async Task LexicalSearchWorksWithoutEmbeddingsAndSuppressesSupersededByDefault()
    {
        using var fixture = await SearchFixture.CreateAsync();
        var store = fixture.Store;
        await fixture.AddDocumentsAsync(
            ProjectionStoreTests.Document("active", "writer lock current guidance"),
            ProjectionStoreTests.Document("old", "writer lock obsolete guidance", MemoryLifecycle.Superseded));

        var searcher = new HybridMemorySearcher(store);
        var ordinary = await searcher.SearchAsync(new MemoryQuery { Text = "writer lock", UseEmbeddings = true });

        Assert.False(ordinary.SemanticSearchUsed);
        Assert.Contains("No embedding provider", ordinary.SemanticSearchDiagnostic, StringComparison.Ordinal);
        Assert.Equal("active", Assert.Single(ordinary.Results).DocumentId);
        Assert.NotNull(await store.InspectAsync("old"));

        var historical = await searcher.SearchAsync(new MemoryQuery
        {
            Text = "writer lock", IncludeSuperseded = true, UseEmbeddings = false
        });
        Assert.Contains(historical.Results, result => result.DocumentId == "old");
    }

    [Fact]
    public async Task HybridSearchUsesQueryInputAndMaximumScoreAcrossDocumentChunks()
    {
        using var fixture = await SearchFixture.CreateAsync();
        var bestChunkDocument = ProjectionStoreTests.Document("best-chunk", "semantic-only-one");
        var averageDocument = ProjectionStoreTests.Document("average", "semantic-only-two");
        await fixture.AddDocumentsAsync(bestChunkDocument, averageDocument);
        var identity = new EmbeddingIdentity
        {
            Provider = "query-probe", Model = "model", Dimensions = 2, Version = "1"
        };
        await fixture.AddEmbeddingsAsync(identity,
            Embedding(bestChunkDocument, identity, 0, 2, [1, 0]),
            Embedding(bestChunkDocument, identity, 1, 2, [0, 1]),
            Embedding(averageDocument, identity, 0, 1, [0.7f, 0.7f]));
        var generator = new QueryGenerator(identity, [0, 1]);

        var response = await new HybridMemorySearcher(fixture.Store, generator).SearchAsync(
            new MemoryQuery { Text = "absenttoken", Limit = 2 });

        Assert.True(response.SemanticSearchUsed);
        Assert.Equal(EmbeddingInputKind.Query, Assert.Single(generator.InputKinds));
        Assert.Equal("best-chunk", response.Results[0].DocumentId);
        Assert.Equal(1d, response.Results[0].Scores.Semantic!.Value, precision: 6);
    }

    [Fact]
    public async Task IdentityMismatchFallsBackToExistingLexicalStateWithDiagnostic()
    {
        using var fixture = await SearchFixture.CreateAsync();
        var document = ProjectionStoreTests.Document("active", "lexical survives provider failure");
        await fixture.AddDocumentsAsync(document);
        var active = new EmbeddingIdentity
        {
            Provider = "query-probe", Model = "active", Dimensions = 2, Version = "1"
        };
        await fixture.AddEmbeddingsAsync(active, Embedding(document, active, 0, 1, [1, 0]));
        var mismatch = active with { Model = "different" };

        var response = await new HybridMemorySearcher(
            fixture.Store,
            new QueryGenerator(mismatch, [1, 0])).SearchAsync(
                new MemoryQuery { Text = "lexical survives" });

        Assert.False(response.SemanticSearchUsed);
        Assert.Equal("active", Assert.Single(response.Results).DocumentId);
        Assert.Contains("InvalidOperationException", response.SemanticSearchDiagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SemanticOrderingCannotBeOverturnedByMetadataAcrossThirtyCandidates()
    {
        using var fixture = await SearchFixture.CreateAsync();
        var documents = Enumerable.Range(0, 30)
            .Select(index => ProjectionStoreTests.Document($"doc{index:00}", $"semantic candidate {index}") with
            {
                Authority = index == 29 ? MemoryAuthority.MintedLesson : MemoryAuthority.UnresolvedRecord,
                SourceTimestamp = DateTimeOffset.UnixEpoch.AddDays(index)
            })
            .ToArray();
        await fixture.AddDocumentsAsync(documents);
        var identity = new EmbeddingIdentity
        {
            Provider = "query-probe", Model = "model", Dimensions = 2, Version = "1"
        };
        var embeddings = documents.Select((document, index) =>
        {
            var similarity = 1f - (index * (0.65f / 29));
            return Embedding(
                document,
                identity,
                0,
                1,
                [similarity, MathF.Sqrt(1 - (similarity * similarity))]);
        }).ToArray();
        await fixture.AddEmbeddingsAsync(identity, embeddings);

        var response = await new HybridMemorySearcher(
            fixture.Store,
            new QueryGenerator(identity, [1, 0])).SearchAsync(
                new MemoryQuery { Text = "no-lexical-overlap", Limit = 30 });

        Assert.True(response.SemanticSearchUsed);
        Assert.Equal("doc00", response.Results[0].DocumentId);
        Assert.Equal(1d, response.Results[0].Scores.Semantic!.Value, precision: 6);
    }

    [Fact]
    public async Task SemanticFloorReportsThatItRemovedEveryCandidate()
    {
        using var fixture = await SearchFixture.CreateAsync();
        var document = ProjectionStoreTests.Document("below-floor", "semantic candidate only");
        await fixture.AddDocumentsAsync(document);
        var identity = new EmbeddingIdentity
        {
            Provider = "query-probe", Model = "model", Dimensions = 2, Version = "1"
        };
        await fixture.AddEmbeddingsAsync(identity, Embedding(document, identity, 0, 1, [0, 1]));

        var response = await new HybridMemorySearcher(
            fixture.Store,
            new QueryGenerator(identity, [1, 0])).SearchAsync(
                new MemoryQuery { Text = "absenttoken" });

        Assert.False(response.SemanticSearchUsed);
        Assert.Empty(response.Results);
        Assert.Contains("filtered all 1 candidates", response.SemanticSearchDiagnostic, StringComparison.Ordinal);
        Assert.Contains("best 0.0000", response.SemanticSearchDiagnostic, StringComparison.Ordinal);
    }

    private static DocumentEmbedding Embedding(
        MemoryDocument document,
        EmbeddingIdentity identity,
        int index,
        int count,
        float[] vector) => new()
    {
        DocumentId = document.Id,
        ContentHash = document.ContentHash,
        Identity = identity,
        ChunkIndex = index,
        ChunkCount = count,
        Vector = vector,
        EmbeddedAt = DateTimeOffset.UnixEpoch
    };

    private sealed class QueryGenerator(EmbeddingIdentity identity, float[] vector) : IEmbeddingGenerator
    {
        public string Provider => identity.Provider;
        public string Model => identity.Model;
        public List<EmbeddingInputKind> InputKinds { get; } = [];

        public Task<EmbeddingBatch> GenerateAsync(EmbeddingRequest request, CancellationToken cancellationToken = default)
        {
            InputKinds.Add(request.InputKind);
            return Task.FromResult(new EmbeddingBatch { Identity = identity, Vectors = [vector] });
        }
    }

    private sealed class SearchFixture : IDisposable
    {
        private readonly TemporaryDirectory _directory;

        private SearchFixture(TemporaryDirectory directory, IMemoryProjectionStore store)
        {
            _directory = directory;
            Store = store;
        }

        public IMemoryProjectionStore Store { get; }

        public static async Task<SearchFixture> CreateAsync()
        {
            var directory = new TemporaryDirectory();
            var factory = new SqliteMemoryProjectionStoreFactory(Path.Combine(directory.Path, "memory.sqlite"));
            await using (var rebuild = await factory.BeginRebuildAsync())
            {
                await rebuild.ReplaceCurrentAsync();
            }

            return new SearchFixture(directory, await factory.OpenCurrentAsync());
        }

        public async Task AddDocumentsAsync(params MemoryDocument[] documents)
        {
            await using var transaction = await Store.BeginUpdateAsync();
            await transaction.ApplyAsync(new SourceDelta
            {
                Source = new SourceDescriptor
                {
                    Id = "events", Kind = CanonicalSourceKind.EventHistory, CanonicalPath = "events.jsonl"
                },
                NextCheckpoint = new EventHistoryCheckpoint
                {
                    SourceId = "events", Kind = CanonicalSourceKind.EventHistory,
                    SourceFingerprint = "fingerprint", ObservedAt = DateTimeOffset.UnixEpoch,
                    FileLength = 1, FileContentHash = "hash", EventCount = 1
                },
                Upserts = documents
            });
            await transaction.CommitAsync();
        }

        public async Task AddEmbeddingsAsync(EmbeddingIdentity identity, params DocumentEmbedding[] embeddings)
        {
            await using var transaction = await Store.BeginUpdateAsync();
            await transaction.SetActiveEmbeddingIdentityAsync(identity);
            await transaction.StoreEmbeddingsAsync(embeddings);
            await transaction.CommitAsync();
        }

        public void Dispose() => _directory.Dispose();
    }
}
