using AILedger.Memory.Contracts;
using AILedger.Memory.Evaluation;
using AILedger.Memory.Storage;
using AILedger.Memory.Tests.Storage;
using AILedger.Memory.Tests.Support;

namespace AILedger.Memory.Tests.Application;

public sealed class EvaluationTests
{
    [Fact]
    public async Task CheckedInEvaluationCorpusIsFixedAndNonEmpty()
    {
        var path = CheckedInCorpusPath();

        var corpus = await EvaluationCorpusLoader.LoadAsync(path);

        Assert.Equal(1, corpus.SchemaVersion);
        Assert.Equal("durable-records-v1", corpus.CorpusVersion);
        Assert.Equal(10, corpus.Cases.Count);
        Assert.Contains(corpus.Cases, item => item.ExpectNoSupportedAnswer);
        Assert.Contains(corpus.Cases, item => item.ExpectedCitationRecordIds.Count > 0);
        Assert.All(
            corpus.Cases.Where(item => item.Kinds.Contains(MemoryDocumentKind.Lesson)),
            item => Assert.NotEmpty(item.OpeningTags));
    }

    [Fact]
    public async Task CheckedInCorpusProducesNonZeroBaselineRankMetrics()
    {
        var path = CheckedInCorpusPath();
        var corpus = await EvaluationCorpusLoader.LoadAsync(path);
        var lessons = new[]
        {
            Lesson("fixture-lesson-alpha", ["durable-memory"], DateTimeOffset.Parse("2026-09-09T00:00:00Z")),
            Lesson("fixture-lesson-beta", ["durable-memory"], DateTimeOffset.Parse("2026-09-08T00:00:00Z"))
        };
        using var fixture = await EvaluationFixture.CreateAsync();

        var report = await new MemoryEvaluator(
                fixture.Store,
                new ScriptedSearcher(query => SearchResponse(query, semanticUsed: false)))
            .EvaluateAsync(
                corpus,
                EvaluationMode.TagRecencyBaseline,
                new TagRecencyBaselineSearcher(lessons));

        Assert.Equal(1d / 9d, report.Metrics.RecallAt1, precision: 12);
        Assert.Equal(1d / 9d, report.Metrics.MeanReciprocalRank, precision: 12);
    }

    [Fact]
    public void EmptyEvaluationCorpusFailsClosed()
    {
        var error = Assert.Throws<InvalidDataException>(() => EvaluationCorpusLoader.Validate(new EvaluationCorpus
        {
            SchemaVersion = 1,
            CorpusVersion = "empty",
            Cases = []
        }));

        Assert.Contains("at least one case", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BaselineUsesExplicitCaseInsensitiveOpeningTagsAndKeepsPolicyRank()
    {
        var weaker = Lesson("aa-weaker", ["Recall"], DateTimeOffset.Parse("2026-09-09T00:00:00Z"));
        var stronger = Lesson("zz-stronger", ["Recall", "Writer-Lock"], DateTimeOffset.Parse("2026-09-08T00:00:00Z"));
        var response = await new TagRecencyBaselineSearcher([weaker, stronger]).SearchAsync(
            new MemoryQuery { Text = "prose that does not contain the tags" },
            ["recall", "writer-lock"]);

        Assert.Equal("zz-stronger", response.Results[0].DocumentId);
        Assert.Equal(2d, response.Results[0].FinalScore);
        Assert.Equal("aa-weaker", response.Results[1].DocumentId);
    }

    [Fact]
    public async Task EvaluatorComputesRankCitationAndAbstentionMetrics()
    {
        using var fixture = await EvaluationFixture.CreateAsync();
        var corpus = new EvaluationCorpus
        {
            SchemaVersion = 1,
            CorpusVersion = "metrics-v1",
            Cases =
            [
                Case("first", "q1", ["expected-1"], ["R1"]),
                Case("second", "q2", ["expected-2"], ["R2"]),
                Case("abstain", "q3", [], [], expectNoSupportedAnswer: true)
            ]
        };
        var searcher = new ScriptedSearcher(query => query.Text switch
        {
            "q1" => SearchResponse(query, false, Result(1, "expected-1", "R1")),
            "q2" => SearchResponse(
                query,
                false,
                Result(1, "distractor", "D0"),
                Result(2, "expected-2", "R2")),
            _ => SearchResponse(query, semanticUsed: false)
        });

        var report = await new MemoryEvaluator(
            fixture.Store,
            searcher,
            () => DateTimeOffset.UnixEpoch,
            () => "evaluation-1").EvaluateAsync(corpus, EvaluationMode.Lexical);

        Assert.Equal(0.5d, report.Metrics.RecallAt1);
        Assert.Equal(1d, report.Metrics.RecallAt5);
        Assert.Equal(0.75d, report.Metrics.MeanReciprocalRank);
        Assert.Equal(1d, report.Metrics.CitationAccuracy);
        Assert.Null(report.EmbeddingIdentity);
        Assert.Equal("evaluation-1", report.EvaluationRunId);
    }

    [Fact]
    public async Task HybridReportNamesIdentityOnlyWhenSemanticSearchContributed()
    {
        using var fixture = await EvaluationFixture.CreateAsync(withIdentity: true);
        var corpus = new EvaluationCorpus
        {
            SchemaVersion = 1,
            CorpusVersion = "identity-v1",
            Cases = [Case("case", "query", ["expected"], ["R1"])]
        };
        var lexicalOnly = new ScriptedSearcher(query =>
            SearchResponse(query, false, Result(1, "expected", "R1")));
        var semantic = new ScriptedSearcher(query =>
            SearchResponse(query, true, Result(1, "expected", "R1")));
        var evaluator = new MemoryEvaluator(fixture.Store, lexicalOnly);

        var lexicalReport = await evaluator.EvaluateAsync(corpus, EvaluationMode.Hybrid);
        var semanticReport = await new MemoryEvaluator(fixture.Store, semantic)
            .EvaluateAsync(corpus, EvaluationMode.Hybrid);

        Assert.Null(lexicalReport.EmbeddingIdentity);
        Assert.Equal(fixture.Identity, semanticReport.EmbeddingIdentity);
    }

    private static EvaluationCase Case(
        string id,
        string query,
        IReadOnlyList<string> expected,
        IReadOnlyList<string> citations,
        bool expectNoSupportedAnswer = false) => new()
    {
        Id = id,
        Query = query,
        ExpectedDocumentIds = expected,
        ExpectedCitationRecordIds = citations,
        ExpectNoSupportedAnswer = expectNoSupportedAnswer
    };

    private static string CheckedInCorpusPath() => Path.Combine(
        Path.GetDirectoryName(typeof(EvaluationTests).Assembly.Location)!,
        "Fixtures",
        "evaluation-cases.json");

    private static MemoryDocument Lesson(string id, IReadOnlyList<string> tags, DateTimeOffset timestamp) =>
        ProjectionStoreTests.Document(id, $"lesson {id}") with
        {
            Kind = MemoryDocumentKind.Lesson,
            Authority = MemoryAuthority.MintedLesson,
            Tags = tags,
            SourceTimestamp = timestamp
        };

    private static MemorySearchResult Result(int rank, string id, string citation) => new()
    {
        Rank = rank,
        DocumentId = id,
        Kind = MemoryDocumentKind.Decision,
        Snippet = id,
        FinalScore = 1,
        Scores = new MemoryScoreComponents(),
        Lifecycle = MemoryLifecycle.Active,
        Authority = MemoryAuthority.AcceptedDecision,
        TaskId = "T1",
        Repository = "AILedger",
        Citations =
        [
            new MemoryCitation
            {
                Kind = MemoryCitationKind.Event,
                SourcePath = "events.jsonl",
                RecordId = citation
            }
        ]
    };

    private static MemorySearchResponse SearchResponse(
        MemoryQuery query,
        bool semanticUsed,
        params MemorySearchResult[] results) => new()
    {
        Query = query,
        Results = results,
        Elapsed = TimeSpan.FromMilliseconds(1),
        SemanticSearchUsed = semanticUsed
    };

    private sealed class ScriptedSearcher(Func<MemoryQuery, MemorySearchResponse> response) : IMemorySearcher
    {
        public Task<MemorySearchResponse> SearchAsync(
            MemoryQuery query,
            CancellationToken cancellationToken = default) => Task.FromResult(response(query));
    }

    private sealed class EvaluationFixture : IDisposable
    {
        private readonly TemporaryDirectory _directory;

        private EvaluationFixture(
            TemporaryDirectory directory,
            IMemoryProjectionStore store,
            EmbeddingIdentity identity)
        {
            _directory = directory;
            Store = store;
            Identity = identity;
        }

        public IMemoryProjectionStore Store { get; }
        public EmbeddingIdentity Identity { get; }

        public static async Task<EvaluationFixture> CreateAsync(bool withIdentity = false)
        {
            var directory = new TemporaryDirectory();
            var factory = new SqliteMemoryProjectionStoreFactory(Path.Combine(directory.Path, "memory.sqlite"));
            await using (var rebuild = await factory.BeginRebuildAsync())
            {
                await rebuild.ReplaceCurrentAsync();
            }

            var store = await factory.OpenCurrentAsync();
            var identity = new EmbeddingIdentity
            {
                Provider = "deterministic", Model = "evaluation", Dimensions = 2, Version = "1"
            };
            if (withIdentity)
            {
                await using var transaction = await store.BeginUpdateAsync();
                await transaction.SetActiveEmbeddingIdentityAsync(identity);
                await transaction.CommitAsync();
            }

            return new EvaluationFixture(directory, store, identity);
        }

        public void Dispose() => _directory.Dispose();
    }
}
