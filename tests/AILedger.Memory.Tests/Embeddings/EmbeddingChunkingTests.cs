using AILedger.Memory.Contracts;
using AILedger.Memory.Application;
using AILedger.Memory.Embeddings;
using AILedger.Memory.Ingestion;
using AILedger.Memory.Storage;
using AILedger.Memory.Tests.Storage;
using AILedger.Memory.Tests.Support;
using AILedger.Core.Contracts;
using AILedger.Storage;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace AILedger.Memory.Tests.Embeddings;

public sealed class EmbeddingChunkingTests
{
    [Fact]
    public void R3_DenseAndUnicodeChunksHonorUtf8ByteLimit()
    {
        var chunker = new DocumentChunker(new DocumentChunkerOptions
        {
            MaximumCharacters = 1_000,
            OverlapCharacters = 4
        });
        var text = string.Concat(Enumerable.Repeat("A+/=", 20)) + "🙂🙂🙂";

        var chunks = chunker.Split(text, maximumUtf8Bytes: 17);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.InRange(Encoding.UTF8.GetByteCount(chunk), 1, 17));
        Assert.DoesNotContain(chunks, chunk =>
            chunk.Length > 0 && (char.IsHighSurrogate(chunk[^1]) || char.IsLowSurrogate(chunk[0])));
    }

    [Fact]
    public void VC3_ProportionalOverlapTracksProviderByteBound()
    {
        var chunker = new DocumentChunker(new DocumentChunkerOptions
        {
            MaximumCharacters = 1_000,
            OverlapCharacters = 100
        });
        var text = string.Concat(Enumerable.Range(0, 250).Select(index => (char)('A' + index % 26)));

        var chunks = chunker.Split(text, maximumUtf8Bytes: 100);

        Assert.Equal([100, 100, 70], chunks.Select(chunk => chunk.Length));
        Assert.Equal(text.Substring(90, 10), chunks[1][..10]);
        Assert.Equal(text.Substring(180, 10), chunks[2][..10]);
    }

    [Fact]
    public async Task R1_R2_OllamaLearnsModelContextAndKeepsTruncationDisabled()
    {
        var requests = new List<CapturedRequest>();
        using var stream = new ScriptedHttpStream(request =>
        {
            requests.Add(request);
            return request.Path switch
            {
                "/api/show" => "{\"model_info\":{\"bert.context_length\":32}}",
                "/api/tags" => "{\"models\":[{\"name\":\"embed:latest\",\"digest\":\"sha256:context\"}]}",
                _ => "{\"embeddings\":[[1,0,0]]}"
            };
        });
        using var transport = new SocketsHttpHandler
        {
            ConnectCallback = (_, _) => ValueTask.FromResult<Stream>(stream)
        };
        using var generator = new OllamaEmbeddingGenerator(transport, OllamaOptions());
        var pipeline = new DocumentEmbeddingPipeline(
            generator,
            new DocumentChunker(new DocumentChunkerOptions
            {
                MaximumCharacters = 1_000,
                OverlapCharacters = 2
            }),
            new DocumentEmbeddingPipelineOptions { BatchSize = 1 });

        var prepared = await pipeline.PrepareAsync(
            [ProjectionStoreTests.Document("context", "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")],
            []);

        Assert.True(prepared.Embeddings.Count > 1);
        Assert.Equal("/api/show", requests[0].Path);
        using (var showPayload = JsonDocument.Parse(requests[0].Body))
        {
            Assert.Equal("embed", showPayload.RootElement.GetProperty("model").GetString());
            Assert.False(showPayload.RootElement.GetProperty("verbose").GetBoolean());
        }

        Assert.All(requests.Where(request => request.Path == "/api/embed"), request =>
        {
            using var payload = JsonDocument.Parse(request.Body);
            Assert.False(payload.RootElement.GetProperty("truncate").GetBoolean());
            var input = Assert.Single(payload.RootElement.GetProperty("input").EnumerateArray()).GetString()!;
            // 32 tokens less the 12.5%/16-token reserve leaves 16 bytes including the five-byte prefix.
            Assert.InRange(Encoding.UTF8.GetByteCount(input), 1, 16);
        });
    }

    [Theory]
    [InlineData(32, 11)]
    [InlineData(64, 43)]
    public async Task R2_ChangingReportedModelContextChangesInputBound(int contextLength, int expectedBytes)
    {
        using var stream = new ScriptedHttpStream(request =>
            $"{{\"model_info\":{{\"bert.context_length\":{contextLength}}}}}");
        using var transport = new SocketsHttpHandler
        {
            ConnectCallback = (_, _) => ValueTask.FromResult<Stream>(stream)
        };
        using var generator = new OllamaEmbeddingGenerator(transport, OllamaOptions());

        var maximumBytes = await generator.ResolveMaximumInputUtf8BytesAsync(EmbeddingInputKind.Document);

        Assert.Equal(expectedBytes, maximumBytes);
    }

    [Fact]
    public async Task VC4_MultipleReportedContextLengthsUseSmallestSafeBound()
    {
        using var stream = new ScriptedHttpStream(_ =>
            "{\"model_info\":{\"general.context_length\":64,\"bert.context_length\":32}}");
        using var transport = new SocketsHttpHandler
        {
            ConnectCallback = (_, _) => ValueTask.FromResult<Stream>(stream)
        };
        using var generator = new OllamaEmbeddingGenerator(transport, OllamaOptions());

        var maximumBytes = await generator.ResolveMaximumInputUtf8BytesAsync(EmbeddingInputKind.Document);

        Assert.Equal(11, maximumBytes);
    }

    [Fact]
    public async Task VC4_MissingPositiveContextLengthStillFails()
    {
        using var stream = new ScriptedHttpStream(_ =>
            "{\"model_info\":{\"bert.context_length\":0,\"bert.embedding_length\":1024}}");
        using var transport = new SocketsHttpHandler
        {
            ConnectCallback = (_, _) => ValueTask.FromResult<Stream>(stream)
        };
        using var generator = new OllamaEmbeddingGenerator(transport, OllamaOptions());

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            generator.ResolveMaximumInputUtf8BytesAsync(EmbeddingInputKind.Document));

        Assert.Contains("positive context length", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task R2_ApplicationPreservesInputLimitResolverThroughRequestCounting()
    {
        using var directory = new TemporaryDirectory();
        var source = await CreateEventSourceAsync(directory.Path, "T-context", "context-bound");
        var generator = new ContextBoundRecordingGenerator(maximumUtf8Bytes: 12);
        var application = new MemoryIndexApplication(
            new SqliteMemoryProjectionStoreFactory(Path.Combine(directory.Path, "memory.sqlite")),
            embeddingGenerator: generator,
            chunker: new DocumentChunker(new DocumentChunkerOptions
            {
                MaximumCharacters = 1_000,
                OverlapCharacters = 2
            }));

        var result = await application.RebuildAsync(new MemoryIndexRequest
        {
            Sources = [source],
            GenerateEmbeddings = true
        });

        Assert.Equal(IndexRunOutcome.Succeeded, result.Statistics.Outcome);
        Assert.True(generator.Inputs.Count > result.Statistics.DocumentsWritten);
        Assert.All(generator.Inputs, input => Assert.InRange(Encoding.UTF8.GetByteCount(input), 1, 12));
    }

    [Fact]
    public async Task R5_LongDocumentsChunkWithoutSilentTruncationAndScoreBestChunk()
    {
        var generator = new RecordingGenerator();
        var pipeline = new DocumentEmbeddingPipeline(
            generator,
            new DocumentChunker(new DocumentChunkerOptions { MaximumCharacters = 20, OverlapCharacters = 4 }),
            new DocumentEmbeddingPipelineOptions { BatchSize = 2 });
        var document = ProjectionStoreTests.Document(
            "long",
            "first segment words second segment words third segment words");

        var prepared = await pipeline.PrepareAsync([document], []);

        Assert.True(prepared.Embeddings.Count > 1);
        Assert.Equal(EmbeddingInputKind.Document, Assert.Single(generator.Kinds));
        Assert.All(prepared.Embeddings, embedding => Assert.Equal(prepared.Embeddings.Count, embedding.ChunkCount));
        Assert.Equal(Enumerable.Range(0, prepared.Embeddings.Count), prepared.Embeddings.Select(item => item.ChunkIndex));
        Assert.Equal(0.9f, SemanticScoring.MaximumChunkScore(new[] { 0.1f, 0.9f, 0.2f }));
    }

    [Fact]
    public void OllamaRejectsUnverifiableTerminalTransportBeforeSendingCanonicalContent()
    {
        Assert.Throws<ArgumentException>(() => new OllamaEmbeddingGenerator(
            new HttpClient(),
            new OllamaEmbeddingOptions { Endpoint = new Uri("https://example.com"), Model = "embed" }));

        var handler = new CountingTerminalHandler();
        var error = Assert.Throws<ArgumentException>(() => new OllamaEmbeddingGenerator(
            handler,
            new OllamaEmbeddingOptions
            {
                Endpoint = new Uri("http://127.0.0.1:11434"), Model = "embed"
            }));

        Assert.Contains("built-in transport", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.SendCount);
    }

    [Fact]
    public void OrdinaryForwardingDelegatingHandlerIsRejectedBeforeAnyRequest()
    {
        var forwardingTerminal = new CountingTerminalHandler();
        using var forwarding = new PassThroughHandler { InnerHandler = forwardingTerminal };
        var forwardingError = Assert.Throws<ArgumentException>(() =>
            new OllamaEmbeddingGenerator(forwarding, OllamaOptions()));

        Assert.Contains("delegating handlers", forwardingError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, forwardingTerminal.SendCount);
    }

    [Fact]
    public void ShortCircuitingDelegatingHandlerIsRejectedBeforeAnyRequest()
    {
        using var shortCircuit = new ShortCircuitHandler();
        var error = Assert.Throws<ArgumentException>(() =>
            new OllamaEmbeddingGenerator(shortCircuit, OllamaOptions()));

        Assert.Contains("delegating handlers", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, shortCircuit.SendCount);
    }

    [Fact]
    public void DelegatingHandlerAroundExactSocketsTransportIsRejectedBeforeReconfigurationOrSend()
    {
        using var sockets = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            UseCookies = true
        };
        using var decorator = new ShortCircuitHandler { InnerHandler = sockets };

        var error = Assert.Throws<ArgumentException>(() =>
            new OllamaEmbeddingGenerator(decorator, OllamaOptions()));

        Assert.Contains("delegating handlers", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, decorator.SendCount);
        Assert.True(sockets.AllowAutoRedirect);
        Assert.True(sockets.UseCookies);
    }

    [Fact]
    public void HttpClientHandlerSubclassesAreRejectedBeforeDirectOrNestedUse()
    {
        using var direct = new DerivedHttpClientHandler();
        var directError = Assert.Throws<ArgumentException>(() => new OllamaEmbeddingGenerator(
            direct,
            OllamaOptions()));

        using var nested = new DerivedHttpClientHandler();
        using var outer = new PassThroughHandler { InnerHandler = nested };
        var nestedError = Assert.Throws<ArgumentException>(() => new OllamaEmbeddingGenerator(
            outer,
            OllamaOptions()));

        Assert.Contains("built-in transport", directError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delegating handlers", nestedError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionOllamaTransportDisablesRedirectsAndCookies()
    {
        using var generator = new OllamaEmbeddingGenerator(OllamaOptions());
        var client = Assert.IsType<HttpClient>(typeof(OllamaEmbeddingGenerator)
            .GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(generator));
        var handlerField = typeof(HttpMessageInvoker)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => typeof(HttpMessageHandler).IsAssignableFrom(field.FieldType));
        var transport = Assert.IsType<SocketsHttpHandler>(handlerField.GetValue(client));

        Assert.False(transport.AllowAutoRedirect);
        Assert.False(transport.UseCookies);
        Assert.False(transport.UseProxy);
        Assert.Null(transport.Proxy);
    }

    [Fact]
    public async Task AcceptedBuiltInTransportsDisableClearAndDoNotConsultConfiguredProxies()
    {
        var socketsProxy = new CountingProxy();
        var requests = new List<CapturedRequest>();
        using var stream = new ScriptedHttpStream(request =>
        {
            requests.Add(request);
            return request.Path == "/api/tags"
                ? "{\"models\":[{\"name\":\"embed:latest\",\"digest\":\"sha256:contained\"}]}"
                : "{\"embeddings\":[[1,0,0]]}";
        });
        using var sockets = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            UseCookies = true,
            UseProxy = true,
            Proxy = socketsProxy,
            ConnectCallback = (_, _) => ValueTask.FromResult<Stream>(stream)
        };
        using var socketsGenerator = new OllamaEmbeddingGenerator(sockets, OllamaOptions());

        Assert.False(sockets.AllowAutoRedirect);
        Assert.False(sockets.UseCookies);
        Assert.False(sockets.UseProxy);
        Assert.Null(sockets.Proxy);

        await socketsGenerator.GenerateAsync(new EmbeddingRequest
        {
            InputKind = EmbeddingInputKind.Document,
            Inputs = ["contained"]
        });

        Assert.Equal(0, socketsProxy.Calls);
        Assert.Equal(["/api/tags", "/api/embed"], requests.Select(request => request.Path));

        var clientProxy = new CountingProxy();
        using var client = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = true,
            UseProxy = true,
            Proxy = clientProxy
        };
        using var clientGenerator = new OllamaEmbeddingGenerator(client, OllamaOptions());

        Assert.False(client.AllowAutoRedirect);
        Assert.False(client.UseCookies);
        Assert.False(client.UseProxy);
        Assert.Null(client.Proxy);
        Assert.Equal(0, clientProxy.Calls);
    }

    [Fact]
    public async Task SuccessiveEmbeddingOperationsResolveFreshModelDigestIdentity()
    {
        var requests = new List<CapturedRequest>();
        var tagRequest = 0;
        using var stream = new ScriptedHttpStream(request =>
        {
            requests.Add(request);
            if (request.Path == "/api/tags")
            {
                tagRequest++;
                return $"{{\"models\":[{{\"name\":\"embed:latest\",\"digest\":\"sha256:build-{tagRequest}\"}}]}}";
            }

            return "{\"embeddings\":[[1,0,0]]}";
        });
        using var transport = new SocketsHttpHandler
        {
            ConnectCallback = (_, _) => ValueTask.FromResult<Stream>(stream)
        };
        using var generator = new OllamaEmbeddingGenerator(transport, OllamaOptions());

        var first = await generator.GenerateAsync(new EmbeddingRequest
        {
            InputKind = EmbeddingInputKind.Document,
            Inputs = ["first"]
        });
        var second = await generator.GenerateAsync(new EmbeddingRequest
        {
            InputKind = EmbeddingInputKind.Document,
            Inputs = ["second"]
        });

        Assert.EndsWith("+model:sha256:build-1", first.Identity.Version, StringComparison.Ordinal);
        Assert.EndsWith("+model:sha256:build-2", second.Identity.Version, StringComparison.Ordinal);
        Assert.NotEqual(first.Identity, second.Identity);
        Assert.Equal(
            ["/api/tags", "/api/embed", "/api/tags", "/api/embed"],
            requests.Select(request => request.Path));
    }

    [Fact]
    public async Task DigestDriftAcrossEmbeddingBatchesIsRejectedBeforeVectorsCanMix()
    {
        var tagRequest = 0;
        using var stream = new ScriptedHttpStream(request =>
        {
            if (request.Path == "/api/tags")
            {
                tagRequest++;
                return $"{{\"models\":[{{\"name\":\"embed:latest\",\"digest\":\"sha256:build-{tagRequest}\"}}]}}";
            }

            return "{\"embeddings\":[[1,0,0]]}";
        });
        using var transport = new SocketsHttpHandler
        {
            ConnectCallback = (_, _) => ValueTask.FromResult<Stream>(stream)
        };
        using var generator = new OllamaEmbeddingGenerator(transport, OllamaOptions());
        var pipeline = new DocumentEmbeddingPipeline(
            generator,
            options: new DocumentEmbeddingPipelineOptions { BatchSize = 1 });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.PrepareAsync(
            [
                ProjectionStoreTests.Document("digest-first", "first content"),
                ProjectionStoreTests.Document("digest-second", "second content")
            ],
            []));

        Assert.Contains("identity", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, tagRequest);
    }

    [Fact]
    public async Task ExactSocketsTransportPreservesOllamaWireContractAndValidatesResponses()
    {
        var requests = new List<CapturedRequest>();
        var embedResponse = 0;
        using var stream = new ScriptedHttpStream(request =>
        {
            requests.Add(request);
            if (request.Path == "/api/tags")
            {
                return "{\"models\":[{\"name\":\"embed:latest\",\"digest\":\"sha256:test-digest\"}]}";
            }

            embedResponse++;
            return embedResponse == 3
                ? "{\"embeddings\":[[1,0,0]]}"
                : request.Body.Contains("query: question", StringComparison.Ordinal)
                    ? "{\"embeddings\":[[0,1,0]]}"
                    : "{\"embeddings\":[[1,0,0],[0,1,0]]}";
        });
        using var transport = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            UseCookies = false,
            ConnectCallback = (_, _) => ValueTask.FromResult<Stream>(stream)
        };
        using var generator = new OllamaEmbeddingGenerator(transport, OllamaOptions());

        Assert.False(transport.AllowAutoRedirect);
        var documents = await generator.GenerateAsync(new EmbeddingRequest
        {
            InputKind = EmbeddingInputKind.Document,
            Inputs = ["first", "second"]
        });
        var query = await generator.GenerateAsync(new EmbeddingRequest
        {
            InputKind = EmbeddingInputKind.Query,
            Inputs = ["question"]
        });
        var wrongCount = await Assert.ThrowsAsync<InvalidDataException>(() => generator.GenerateAsync(
            new EmbeddingRequest
            {
                InputKind = EmbeddingInputKind.Document,
                Inputs = ["third", "fourth"]
            }));

        Assert.Equal(3, documents.Identity.Dimensions);
        Assert.Equal("ollama", documents.Identity.Provider);
        Assert.Equal("embed", documents.Identity.Model);
        Assert.EndsWith("+model:sha256:test-digest", documents.Identity.Version, StringComparison.Ordinal);
        Assert.Equal(documents.Identity, query.Identity);
        Assert.Contains("1 vectors for 2 inputs", wrongCount.Message, StringComparison.Ordinal);
        Assert.Collection(
            requests,
            AssertTagsRequest,
            firstEmbed => AssertEmbedRequest(firstEmbed, "doc: first", "doc: second"),
            AssertTagsRequest,
            queryEmbed => AssertEmbedRequest(queryEmbed, "query: question"),
            AssertTagsRequest,
            wrongEmbed => AssertEmbedRequest(wrongEmbed, "doc: third", "doc: fourth"));
    }

    [Fact]
    public void NestedUnverifiableTerminalTransportIsRejectedBeforeAnySend()
    {
        var terminal = new CountingTerminalHandler();
        using var outer = new PassThroughHandler { InnerHandler = terminal };

        var error = Assert.Throws<ArgumentException>(() => new OllamaEmbeddingGenerator(
            outer,
            new OllamaEmbeddingOptions
            {
                Endpoint = new Uri("http://127.0.0.1:11434"), Model = "embed"
            }));

        Assert.Contains("delegating handlers", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, terminal.SendCount);
    }

    [Fact]
    public async Task DeterministicIdentityRemainsStableAcrossSourcesAndIncrementalReuse()
    {
        using var directory = new TemporaryDirectory();
        var first = await CreateEventSourceAsync(directory.Path, "T1", "first");
        var second = await CreateEventSourceAsync(directory.Path, "T2", "second");
        var factory = new SqliteMemoryProjectionStoreFactory(Path.Combine(directory.Path, "memory.sqlite"));
        var generator = new DeterministicEmbeddingGenerator(dimensions: 8);
        var application = new MemoryIndexApplication(factory, embeddingGenerator: generator);
        var request = new MemoryIndexRequest { Sources = [first, second], GenerateEmbeddings = true };

        var rebuild = await application.RebuildAsync(request);
        Assert.Equal(IndexRunOutcome.Succeeded, rebuild.Statistics.Outcome);
        Assert.Equal(generator.Identity, rebuild.Statistics.EmbeddingIdentity);

        var original = await File.ReadAllTextAsync(first.CanonicalPath);
        await File.WriteAllTextAsync(first.CanonicalPath, original.TrimEnd('\n') + " \n");

        var update = await application.UpdateAsync(request);
        Assert.Equal(IndexRunOutcome.Succeeded, update.Statistics.Outcome);
        Assert.Equal(generator.Identity, update.Statistics.EmbeddingIdentity);
        Assert.True(update.Statistics.EmbeddingsReused > 0);
        Assert.Equal(0, update.Statistics.EmbeddingsWritten);
    }

    [Fact]
    public async Task EmbeddingBatchFailurePreservesLexicalStateAndRecordsDegradedStatistics()
    {
        using var directory = new TemporaryDirectory();
        var source = await CreateEventSourceAsync(directory.Path, "T1", "failure");
        var factory = new SqliteMemoryProjectionStoreFactory(Path.Combine(directory.Path, "memory.sqlite"));
        var generator = new ThrowingGenerator();
        var application = new MemoryIndexApplication(factory, embeddingGenerator: generator);

        var result = await application.RebuildAsync(new MemoryIndexRequest
        {
            Sources = [source],
            GenerateEmbeddings = true
        });

        Assert.Equal(IndexRunOutcome.PartiallyEmbedded, result.Statistics.Outcome);
        Assert.Equal(1, result.Statistics.EmbeddingRequests);
        Assert.Equal("embedding-unavailable", result.Statistics.FailureCode);
        Assert.Contains("planned embedding failure", result.Statistics.FailureMessage, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("lexical documents were retained", StringComparison.Ordinal));
        Assert.Equal(1, generator.Requests);

        var response = await application.SearchAsync(new MemoryQuery
        {
            Text = "Durable memory content failure",
            UseEmbeddings = false
        });
        Assert.Contains(response.Results, item => item.Kind == MemoryDocumentKind.Artifact);

        var persisted = await application.GetStatisticsAsync();
        Assert.Equal(IndexRunOutcome.PartiallyEmbedded, persisted.LatestRun!.Outcome);
        Assert.Equal("embedding-unavailable", persisted.LatestRun.FailureCode);
        Assert.Equal(1, persisted.LatestRun.EmbeddingRequests);
    }

    [Fact]
    public async Task TwoComposedRebuildsProduceIdenticalDocumentIdsAndContentHashes()
    {
        using var directory = new TemporaryDirectory();
        var first = await CreateEventSourceAsync(directory.Path, "T1", "stable-first");
        var second = await CreateEventSourceAsync(directory.Path, "T2", "stable-second");
        var database = Path.Combine(directory.Path, "memory.sqlite");
        var application = new MemoryIndexApplication(new SqliteMemoryProjectionStoreFactory(database));
        var request = new MemoryIndexRequest
        {
            Sources = [first, second],
            GenerateEmbeddings = false
        };

        await application.RebuildAsync(request);
        var firstProjection = await ReadDocumentIdentitySnapshotAsync(database);
        await application.RebuildAsync(request);
        var secondProjection = await ReadDocumentIdentitySnapshotAsync(database);

        Assert.NotEmpty(firstProjection);
        Assert.Equal(firstProjection, secondProjection);
    }

    [Fact]
    public async Task GeneratorBackedNoOpUpdateRequestsZeroEmbeddings()
    {
        using var directory = new TemporaryDirectory();
        var source = await CreateEventSourceAsync(directory.Path, "T1", "no-op");
        var factory = new SqliteMemoryProjectionStoreFactory(Path.Combine(directory.Path, "memory.sqlite"));
        var generator = new RecordingGenerator();
        var application = new MemoryIndexApplication(factory, embeddingGenerator: generator);
        var request = new MemoryIndexRequest { Sources = [source], GenerateEmbeddings = true };

        var rebuild = await application.RebuildAsync(request);
        Assert.True(rebuild.Statistics.EmbeddingRequests > 0);
        generator.Reset();

        var update = await application.UpdateAsync(request);

        Assert.Equal(IndexRunOutcome.Succeeded, update.Statistics.Outcome);
        Assert.Equal(0, update.Statistics.DocumentsWritten);
        Assert.Equal(0, update.Statistics.EmbeddingRequests);
        Assert.Equal(0, generator.Requests);
    }

    [Fact]
    public async Task RequiredIdentityRefusesGeneratorWrapperThatCannotResolveModelBuild()
    {
        var identity = new EmbeddingIdentity
        {
            Provider = "recording", Model = "recording-v1", Dimensions = 3, Version = "1"
        };
        var wrapped = new NonResolvingWrapper(new RecordingGenerator());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DocumentEmbeddingPipeline(wrapped).PrepareAsync(
                [ProjectionStoreTests.Document("stable", "unchanged content")],
                [],
                identity));

        Assert.Contains("cannot verify", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, wrapped.Requests);
    }

    private sealed class RecordingGenerator : IEmbeddingGenerator
    {
        public string Provider => "recording";
        public string Model => "recording-v1";
        public HashSet<EmbeddingInputKind> Kinds { get; } = [];
        public int Requests { get; private set; }

        public void Reset()
        {
            Kinds.Clear();
            Requests = 0;
        }

        public Task<EmbeddingBatch> GenerateAsync(EmbeddingRequest request, CancellationToken cancellationToken = default)
        {
            Requests++;
            Kinds.Add(request.InputKind);
            return Task.FromResult(new EmbeddingBatch
            {
                Identity = new EmbeddingIdentity { Provider = Provider, Model = Model, Dimensions = 3, Version = "1" },
                Vectors = request.Inputs.Select(_ => (ReadOnlyMemory<float>)new float[] { 1, 0, 0 }).ToArray()
            });
        }
    }

    private sealed class ThrowingGenerator : IEmbeddingGenerator
    {
        public string Provider => "throwing";
        public string Model => "planned-failure";
        public int Requests { get; private set; }

        public Task<EmbeddingBatch> GenerateAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests++;
            throw new InvalidOperationException("planned embedding failure");
        }
    }

    private sealed class ContextBoundRecordingGenerator(int maximumUtf8Bytes) :
        IEmbeddingGenerator,
        IEmbeddingIdentityResolver,
        IEmbeddingInputLimitResolver
    {
        public string Provider => "context-bound";
        public string Model => "context-bound-v1";
        public List<string> Inputs { get; } = [];

        public Task<int> ResolveMaximumInputUtf8BytesAsync(
            EmbeddingInputKind inputKind,
            CancellationToken cancellationToken = default) => Task.FromResult(maximumUtf8Bytes);

        public Task<EmbeddingIdentity> ResolveIdentityAsync(
            int dimensions,
            CancellationToken cancellationToken = default) => Task.FromResult(Identity());

        public Task<EmbeddingBatch> GenerateAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default)
        {
            Inputs.AddRange(request.Inputs);
            return Task.FromResult(new EmbeddingBatch
            {
                Identity = Identity(),
                Vectors = request.Inputs.Select(_ => (ReadOnlyMemory<float>)new float[] { 1, 0, 0 }).ToArray()
            });
        }

        private EmbeddingIdentity Identity() => new()
        {
            Provider = Provider,
            Model = Model,
            Dimensions = 3,
            Version = "1"
        };
    }

    private static async Task<IReadOnlyList<string>> ReadDocumentIdentitySnapshotAsync(string database)
    {
        var values = new List<string>();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, content_hash FROM documents ORDER BY id";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add($"{reader.GetString(0)}:{reader.GetString(1)}");
        }

        return values;
    }

    private static async Task<SourceDescriptor> CreateEventSourceAsync(
        string root,
        string taskId,
        string suffix)
    {
        var path = Path.Combine(root, $"{taskId}.jsonl");
        var actor = new ActorId("operator");
        var events = new[]
        {
            new LedgerEvent(
                1,
                new EventId($"EV-{suffix}-1"),
                new TaskId(taskId),
                actor,
                DateTimeOffset.UnixEpoch,
                null,
                $"correlation-{suffix}-1",
                new TaskOpened($"Fixture {suffix}", $"Memory identity {suffix}")),
            new LedgerEvent(
                1,
                new EventId($"EV-{suffix}-2"),
                new TaskId(taskId),
                actor,
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                null,
                $"correlation-{suffix}-2",
                new ArtifactRecorded(new GovernedArtifact(
                    new ArtifactId($"A-{suffix}"),
                    GovernedArtifactKind.UserRequest,
                    $"Artifact {suffix}",
                    $"Durable memory content {suffix}",
                    null,
                    null,
                    null,
                    new Provenance(actor, DateTimeOffset.UnixEpoch.AddSeconds(1), "artifact.record"))))
        };
        await File.WriteAllTextAsync(
            path,
            string.Join('\n', events.Select(item =>
                JsonSerializer.Serialize(item, LedgerJson.CreateOptions()))) + "\n");
        return new SourceDescriptor
        {
            Id = $"source-{suffix}",
            Kind = CanonicalSourceKind.EventHistory,
            CanonicalPath = path,
            TaskId = taskId
        };
    }

    private sealed class CountingTerminalHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            throw new InvalidOperationException("The fail-closed constructor must prevent this send.");
        }
    }

    private sealed class PassThroughHandler : DelegatingHandler;

    private sealed class ShortCircuitHandler : DelegatingHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private sealed class DerivedHttpClientHandler : HttpClientHandler;

    private sealed class CountingProxy : System.Net.IWebProxy
    {
        public int Calls { get; private set; }

        public System.Net.ICredentials? Credentials { get; set; }

        public Uri GetProxy(Uri destination)
        {
            Calls++;
            return new Uri("http://192.0.2.1:8080");
        }

        public bool IsBypassed(Uri host)
        {
            Calls++;
            return false;
        }
    }

    private static OllamaEmbeddingOptions OllamaOptions() => new()
    {
        Endpoint = new Uri("http://127.0.0.1:11434"),
        Model = "embed",
        DocumentPrefix = "doc: ",
        QueryPrefix = "query: "
    };

    private static void AssertTagsRequest(CapturedRequest request)
    {
        Assert.Equal("GET", request.Method);
        Assert.Equal("/api/tags", request.Path);
    }

    private static void AssertEmbedRequest(CapturedRequest request, params string[] expectedInputs)
    {
        Assert.Equal("POST", request.Method);
        Assert.Equal("/api/embed", request.Path);
        using var payload = JsonDocument.Parse(request.Body);
        Assert.Equal("embed", payload.RootElement.GetProperty("model").GetString());
        Assert.False(payload.RootElement.GetProperty("truncate").GetBoolean());
        Assert.Equal(
            expectedInputs,
            payload.RootElement.GetProperty("input").EnumerateArray().Select(item => item.GetString()));
    }

    private sealed record CapturedRequest(string Method, string Path, string Body);

    private sealed class ScriptedHttpStream(Func<CapturedRequest, string> respond) : Stream
    {
        private readonly List<byte> _requestBytes = [];
        private readonly Channel<byte[]> _responses = Channel.CreateUnbounded<byte[]>();
        private byte[]? _currentResponse;
        private int _currentOffset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_currentResponse is null)
            {
                _currentResponse = await _responses.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                _currentOffset = 0;
            }

            var count = Math.Min(buffer.Length, _currentResponse.Length - _currentOffset);
            _currentResponse.AsMemory(_currentOffset, count).CopyTo(buffer);
            _currentOffset += count;
            if (_currentOffset == _currentResponse.Length)
            {
                _currentResponse = null;
            }

            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _requestBytes.AddRange(buffer.ToArray());
            QueueCompleteRequests();
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _responses.Writer.TryComplete();
            }

            base.Dispose(disposing);
        }

        private void QueueCompleteRequests()
        {
            while (TryTakeRequest(out var request))
            {
                var body = Encoding.UTF8.GetBytes(respond(request));
                var headers = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: keep-alive\r\n\r\n");
                var response = new byte[headers.Length + body.Length];
                headers.CopyTo(response, 0);
                body.CopyTo(response, headers.Length);
                Assert.True(_responses.Writer.TryWrite(response));
            }
        }

        private bool TryTakeRequest(out CapturedRequest request)
        {
            request = null!;
            var bytes = _requestBytes.ToArray();
            var headerEnd = FindHeaderEnd(bytes);
            if (headerEnd < 0)
            {
                return false;
            }

            var headers = Encoding.ASCII.GetString(bytes, 0, headerEnd);
            var lines = headers.Split("\r\n", StringSplitOptions.None);
            var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestLine.Length < 2)
            {
                return false;
            }

            var isChunked = lines.Any(line =>
                line.Equals("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase));
            var contentLength = lines
                .Where(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                .Select(line => int.Parse(line[(line.IndexOf(':') + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture))
                .DefaultIfEmpty()
                .Single();
            var bodyOffset = headerEnd + 4;
            int requestLength;
            string body;
            if (isChunked)
            {
                if (!TryDecodeChunkedBody(bytes, bodyOffset, out requestLength, out body))
                {
                    return false;
                }
            }
            else
            {
                requestLength = bodyOffset + contentLength;
                if (bytes.Length < requestLength)
                {
                    return false;
                }

                body = Encoding.UTF8.GetString(bytes, bodyOffset, contentLength);
            }

            request = new CapturedRequest(
                requestLine[0],
                requestLine[1],
                body);
            _requestBytes.RemoveRange(0, requestLength);
            return true;
        }

        private static bool TryDecodeChunkedBody(
            byte[] bytes,
            int bodyOffset,
            out int requestLength,
            out string body)
        {
            requestLength = 0;
            body = string.Empty;
            var position = bodyOffset;
            using var decoded = new MemoryStream();
            while (true)
            {
                var sizeEnd = FindCrlf(bytes, position);
                if (sizeEnd < 0 || !int.TryParse(
                        Encoding.ASCII.GetString(bytes, position, sizeEnd - position).Split(';')[0],
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var chunkLength))
                {
                    return false;
                }

                position = sizeEnd + 2;
                if (bytes.Length < position + chunkLength + 2)
                {
                    return false;
                }

                if (chunkLength == 0)
                {
                    requestLength = position + 2;
                    body = Encoding.UTF8.GetString(decoded.ToArray());
                    return true;
                }

                decoded.Write(bytes, position, chunkLength);
                position += chunkLength;
                if (bytes[position] != '\r' || bytes[position + 1] != '\n')
                {
                    throw new InvalidDataException("Malformed chunked HTTP request in the test transport.");
                }

                position += 2;
            }
        }

        private static int FindCrlf(byte[] bytes, int start)
        {
            for (var index = start; index <= bytes.Length - 2; index++)
            {
                if (bytes[index] == '\r' && bytes[index + 1] == '\n')
                {
                    return index;
                }
            }

            return -1;
        }

        private static int FindHeaderEnd(byte[] bytes)
        {
            for (var index = 0; index <= bytes.Length - 4; index++)
            {
                if (bytes[index] == '\r' && bytes[index + 1] == '\n' &&
                    bytes[index + 2] == '\r' && bytes[index + 3] == '\n')
                {
                    return index;
                }
            }

            return -1;
        }
    }

    private sealed class NonResolvingWrapper(IEmbeddingGenerator inner) : IEmbeddingGenerator
    {
        public string Provider => inner.Provider;
        public string Model => inner.Model;
        public int Requests { get; private set; }

        public Task<EmbeddingBatch> GenerateAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests++;
            return inner.GenerateAsync(request, cancellationToken);
        }
    }
}
