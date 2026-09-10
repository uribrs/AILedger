using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AILedger.Memory.Contracts;

namespace AILedger.Memory.Embeddings;

public sealed record OllamaEmbeddingOptions
{
    public Uri Endpoint { get; init; } = new("http://127.0.0.1:11434/");
    public required string Model { get; init; }
    public string Version { get; init; } = "ollama-api-embed-v1";
    public string? KeepAlive { get; init; }
    public string DocumentPrefix { get; init; } = string.Empty;
    public string QueryPrefix { get; init; } = string.Empty;
}

/// <summary>Resolves a generator identity before stored vectors are considered for reuse.</summary>
public interface IEmbeddingIdentityResolver
{
    Task<EmbeddingIdentity> ResolveIdentityAsync(
        int dimensions,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves a conservative provider-specific byte bound before inputs are chunked.</summary>
public interface IEmbeddingInputLimitResolver
{
    Task<int> ResolveMaximumInputUtf8BytesAsync(
        EmbeddingInputKind inputKind,
        CancellationToken cancellationToken = default);
}

/// <summary>Loopback-only adapter for Ollama's native batch embedding endpoint.</summary>
public sealed class OllamaEmbeddingGenerator :
    IEmbeddingGenerator,
    IEmbeddingIdentityResolver,
    IEmbeddingInputLimitResolver,
    IDisposable
{
    private const int MinimumContextReserveTokens = 16;
    private const int ContextReserveDivisor = 8;
    private readonly HttpClient _httpClient;
    private readonly OllamaEmbeddingOptions _options;
    private readonly Uri _embedEndpoint;
    private readonly Uri _tagsEndpoint;
    private readonly Uri _showEndpoint;
    private readonly bool _ownsHttpClient;

    public OllamaEmbeddingGenerator(OllamaEmbeddingOptions options)
        : this(CreateRedirectSafeClient(), options, ownsHttpClient: true)
    {
    }

    public OllamaEmbeddingGenerator(HttpMessageHandler transport, OllamaEmbeddingOptions options)
        : this(CreateRedirectSafeClient(transport), options, ownsHttpClient: true)
    {
    }

    public OllamaEmbeddingGenerator(HttpClient httpClient, OllamaEmbeddingOptions options)
    {
        throw new ArgumentException(
            "An injected HttpClient cannot guarantee redirect containment. Pass options or a terminal HttpMessageHandler.",
            nameof(httpClient));
    }

    private OllamaEmbeddingGenerator(HttpClient httpClient, OllamaEmbeddingOptions options, bool ownsHttpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        if (!IsLoopback(options.Endpoint))
        {
            throw new ArgumentException("The Ollama endpoint must use HTTP on a loopback address.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new ArgumentException("An Ollama model name is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Version))
        {
            throw new ArgumentException("An embedding adapter version is required.", nameof(options));
        }

        _httpClient = httpClient;
        _options = options;
        _ownsHttpClient = ownsHttpClient;
        _embedEndpoint = options.Endpoint.AbsolutePath.EndsWith("/api/embed", StringComparison.Ordinal)
            ? options.Endpoint
            : new Uri(EnsureTrailingSlash(options.Endpoint), "api/embed");
        _tagsEndpoint = options.Endpoint.AbsolutePath.EndsWith("/api/embed", StringComparison.Ordinal)
            ? new Uri(options.Endpoint, "./tags")
            : new Uri(EnsureTrailingSlash(options.Endpoint), "api/tags");
        _showEndpoint = options.Endpoint.AbsolutePath.EndsWith("/api/embed", StringComparison.Ordinal)
            ? new Uri(options.Endpoint, "./show")
            : new Uri(EnsureTrailingSlash(options.Endpoint), "api/show");
    }

    public string Provider => "ollama";

    public string Model => _options.Model;

    public async Task<EmbeddingIdentity> ResolveIdentityAsync(
        int dimensions,
        CancellationToken cancellationToken = default)
    {
        var digest = await GetModelDigestAsync(cancellationToken).ConfigureAwait(false);
        var identity = CreateIdentity(dimensions, digest);
        identity.Validate();
        return identity;
    }

    public async Task<EmbeddingBatch> GenerateAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Inputs);
        if (request.Inputs.Count == 0)
        {
            throw new ArgumentException("At least one embedding input is required.", nameof(request));
        }

        var prefix = request.InputKind == EmbeddingInputKind.Document
            ? _options.DocumentPrefix
            : _options.QueryPrefix;
        var inputs = request.Inputs
            .Select(input => prefix + (input ?? throw new ArgumentException(
                "Embedding inputs cannot contain null values.", nameof(request))))
            .ToArray();

        var digest = await GetModelDigestAsync(cancellationToken).ConfigureAwait(false);
        using var response = await _httpClient.PostAsJsonAsync(
            _embedEndpoint,
            new OllamaEmbedRequest
            {
                Model = _options.Model,
                Input = inputs,
                Truncate = false,
                KeepAlive = _options.KeepAlive
            },
            cancellationToken).ConfigureAwait(false);

        EnsureLoopbackResponse(response);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(
            cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Ollama returned an empty embedding response.");

        if (payload.Embeddings is null || payload.Embeddings.Count != inputs.Length)
        {
            throw new InvalidDataException(
                $"Ollama returned {payload.Embeddings?.Count ?? 0} vectors for {inputs.Length} inputs.");
        }

        var dimensions = payload.Embeddings.Count == 0 ? 0 : payload.Embeddings[0].Length;
        var identity = CreateIdentity(dimensions, digest);
        identity.Validate();

        var vectors = new ReadOnlyMemory<float>[payload.Embeddings.Count];
        for (var index = 0; index < payload.Embeddings.Count; index++)
        {
            if (payload.Embeddings[index].Length != dimensions)
            {
                throw new InvalidDataException("Ollama returned vectors with inconsistent dimensions.");
            }

            vectors[index] = EmbeddingVector.Normalize(payload.Embeddings[index]);
        }

        return new EmbeddingBatch
        {
            Identity = identity,
            Vectors = vectors
        };
    }

    public async Task<int> ResolveMaximumInputUtf8BytesAsync(
        EmbeddingInputKind inputKind,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            _showEndpoint,
            new OllamaShowRequest { Model = _options.Model },
            cancellationToken).ConfigureAwait(false);
        EnsureLoopbackResponse(response);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OllamaShowResponse>(
            cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Ollama returned an empty model-details response.");
        var contextLength = ReadContextLength(payload.ModelInfo);

        // UTF-8 bytes are a conservative tokenizer-independent upper bound for input tokens,
        // including dense base64/minified JSON where the usual four-characters-per-token estimate fails.
        // Reserve 12.5% (at least 16 tokens) for special tokens and tokenizer/provider overhead.
        var reserve = Math.Max(MinimumContextReserveTokens, (contextLength + ContextReserveDivisor - 1) / ContextReserveDivisor);
        var prefix = inputKind == EmbeddingInputKind.Document ? _options.DocumentPrefix : _options.QueryPrefix;
        var maximumBytes = contextLength - reserve - Encoding.UTF8.GetByteCount(prefix);
        if (maximumBytes <= 0)
        {
            throw new InvalidDataException(
                $"Ollama model '{_options.Model}' context length {contextLength} leaves no input capacity after the safety reserve and prefix.");
        }

        return maximumBytes;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private EmbeddingIdentity CreateIdentity(int dimensions, string digest) => new()
    {
        Provider = Provider,
        Model = Model,
        Dimensions = dimensions,
        Version = $"{_options.Version}+model:{digest}"
    };

    private async Task<string> GetModelDigestAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(_tagsEndpoint, cancellationToken).ConfigureAwait(false);
        EnsureLoopbackResponse(response);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(
            cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Ollama returned an empty model-list response.");
        var model = payload.Models?.FirstOrDefault(item =>
            IsConfiguredModel(item.Name) || IsConfiguredModel(item.Model));
        if (string.IsNullOrWhiteSpace(model?.Digest))
        {
            throw new InvalidDataException(
                $"Ollama did not report a digest for configured model '{_options.Model}'.");
        }

        return model.Digest;
    }

    private bool IsConfiguredModel(string? candidate)
        => string.Equals(candidate, _options.Model, StringComparison.Ordinal) ||
           (!_options.Model.Contains(":", StringComparison.Ordinal) &&
            string.Equals(candidate, $"{_options.Model}:latest", StringComparison.Ordinal));

    private static int ReadContextLength(IReadOnlyDictionary<string, JsonElement>? modelInfo)
    {
        var values = modelInfo?
            .Where(item => item.Key.EndsWith(".context_length", StringComparison.Ordinal))
            .Select(item => item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetInt32(out var value)
                ? value
                : 0)
            .Where(value => value > 0)
            .ToArray() ?? [];
        // Multiple architecture-specific limits can be reported. The smallest is the
        // safe deterministic bound: undersizing costs chunks, while oversizing restores
        // the provider 400s that this limit exists to prevent.
        return values.Length > 0
            ? values.Min()
            : throw new InvalidDataException(
                "Ollama model details did not report a positive context length.");
    }

    private static HttpClient CreateRedirectSafeClient()
        => new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            Proxy = null
        }, disposeHandler: true);

    private static HttpClient CreateRedirectSafeClient(HttpMessageHandler transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ContainTransport(transport);
        return new HttpClient(transport, disposeHandler: true);
    }

    private static void ContainTransport(HttpMessageHandler handler)
    {
        switch (handler)
        {
            case SocketsHttpHandler sockets when handler.GetType() == typeof(SocketsHttpHandler):
                sockets.AllowAutoRedirect = false;
                sockets.UseCookies = false;
                sockets.UseProxy = false;
                sockets.Proxy = null;
                return;
            case HttpClientHandler client when handler.GetType() == typeof(HttpClientHandler):
                client.AllowAutoRedirect = false;
                client.UseCookies = false;
                client.UseProxy = false;
                client.Proxy = null;
                return;
            case DelegatingHandler:
                throw new ArgumentException(
                    "Injected delegating handlers are not permitted because their send behavior cannot be verified.",
                    nameof(handler));
            default:
                throw new ArgumentException(
                    "The injected HTTP handler must be an exact built-in transport whose redirect behavior can be disabled.",
                    nameof(handler));
        }
    }

    private static void EnsureLoopbackResponse(HttpResponseMessage response)
    {
        var responseUri = response.RequestMessage?.RequestUri;
        if (responseUri is not null && !IsLoopback(responseUri))
        {
            throw new HttpRequestException("Ollama redirected the request away from loopback.");
        }

        if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest)
        {
            throw new HttpRequestException("Ollama redirects are not permitted.");
        }
    }

    private static bool IsLoopback(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttp ||
            !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            return false;
        }

        return endpoint.IsLoopback;
    }

    private static Uri EnsureTrailingSlash(Uri endpoint)
    {
        var builder = new UriBuilder(endpoint);
        if (!builder.Path.EndsWith("/", StringComparison.Ordinal))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }

    private sealed record OllamaEmbedRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("input")]
        public required IReadOnlyList<string> Input { get; init; }

        [JsonPropertyName("truncate")]
        public required bool Truncate { get; init; }

        [JsonPropertyName("keep_alive")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? KeepAlive { get; init; }
    }

    private sealed record OllamaShowRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("verbose")]
        public bool Verbose { get; init; }
    }

    private sealed record OllamaShowResponse
    {
        [JsonPropertyName("model_info")]
        public IReadOnlyDictionary<string, JsonElement>? ModelInfo { get; init; }
    }

    private sealed record OllamaEmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public IReadOnlyList<float[]>? Embeddings { get; init; }
    }

    private sealed record OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public IReadOnlyList<OllamaModel>? Models { get; init; }
    }

    private sealed record OllamaModel
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }
}
