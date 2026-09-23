using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace AILedger.Cli.Verification;

/// <summary>The production engine transport: HTTP over the Docker unix socket.</summary>
internal static class UnixSocketEngineHandler
{
    public static HttpMessageHandler Create(string socketPath) => new SocketsHttpHandler
    {
        ConnectCallback = async (_, cancellationToken) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken)
                    .ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };

    /// <summary>
    /// The socket path a <c>unix://</c> DOCKER_HOST names, or null for any other scheme. Only unix
    /// sockets are supported in v1 (K1: macOS with Colima).
    /// </summary>
    public static string? SocketPath(string dockerHost) =>
        dockerHost.StartsWith("unix://", StringComparison.Ordinal) && dockerHost.Length > "unix://".Length
            ? dockerHost["unix://".Length..]
            : null;
}

/// <summary>
/// The three Docker engine calls verification makes: ping, list by label, and remove by id.
/// Nothing else here deletes anything (contract S8).
/// </summary>
internal sealed class DockerEngineClient : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient _http;

    public DockerEngineClient(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("http://localhost/"),
            Timeout = RequestTimeout
        };
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("_ping", cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.OK &&
               string.Equals(body.Trim(), "OK", StringComparison.Ordinal);
    }

    /// <summary>The ids of every container, running or not, that carries exactly this label.</summary>
    public async Task<IReadOnlyList<string>> ListByLabelAsync(string label, CancellationToken cancellationToken)
    {
        var filter = JsonSerializer.Serialize(new Dictionary<string, string[]> { ["label"] = [label] });
        using var response = await _http.GetAsync(
            $"containers/json?all=true&filters={Uri.EscapeDataString(filter)}", cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Listing containers returned {(int)response.StatusCode}: {Bounded(body)}");
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.EnumerateArray()
            .Select(container => container.GetProperty("Id").GetString())
            .OfType<string>()
            .ToArray();
    }

    public async Task RemoveAsync(string containerId, CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync(
            $"containers/{Uri.EscapeDataString(containerId)}?force=true&v=true", cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Removing container returned {(int)response.StatusCode}: {Bounded(body)}");
        }
    }

    public void Dispose() => _http.Dispose();

    private static string Bounded(string text) => text.Length <= 500 ? text.Trim() : text[..500].Trim() + "…";
}

/// <summary>Contract S7: can this process reach the Docker engine through DOCKER_HOST?</summary>
internal static class DockerSocketPreflight
{
    /// <summary>Null when the engine answered <c>OK</c> to <c>GET /_ping</c>; otherwise the cause.</summary>
    public static async Task<string?> CheckAsync(
        string dockerHost,
        Func<string, HttpMessageHandler> createHandler,
        CancellationToken cancellationToken)
    {
        if (UnixSocketEngineHandler.SocketPath(dockerHost) is not { } socketPath)
        {
            return $"DOCKER_HOST '{dockerHost}' is not a unix:// socket; only unix sockets are supported";
        }

        if (!File.Exists(socketPath))
        {
            return $"Docker socket '{socketPath}' does not exist: Colima is not started, or DOCKER_HOST names another path";
        }

        try
        {
            using var client = new DockerEngineClient(createHandler(socketPath));
            return await client.PingAsync(cancellationToken).ConfigureAwait(false)
                ? null
                : Stopped(socketPath);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                                          exception is HttpRequestException or SocketException or
                                              UnauthorizedAccessException or IOException or
                                              TaskCanceledException)
        {
            return IsPermissionDenied(exception)
                ? $"Docker socket '{socketPath}' refused this process: this process cannot reach the socket; it is probably sandboxed; run from a host shell"
                : Stopped(socketPath);
        }
    }

    private static string Stopped(string socketPath) =>
        $"Docker socket '{socketPath}' gave no OK: Colima is stopped or not responding";

    private static bool IsPermissionDenied(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException ||
                current is SocketException { SocketErrorCode: SocketError.AccessDenied })
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Contract S5 cleanup record.</summary>
internal sealed record ContainerCleanupRecord(
    string Label,
    IReadOnlyList<string> Removed,
    IReadOnlyList<ContainerCleanupFailure> Failed);

/// <summary>A cleanup call that failed. A null id means the label list itself failed.</summary>
internal sealed record ContainerCleanupFailure(string? Id, string Error);

/// <summary>
/// Contract S8: removes the containers the engine returns for the exact owner label, and only
/// those. Never prune, never diff (PALT4). A failure is recorded and is not fatal.
/// </summary>
internal static class ContainerCleanup
{
    public const string LabelKey = "ailedger.run";

    public static async Task<ContainerCleanupRecord> RunAsync(
        HttpMessageHandler handler,
        string verificationRun,
        CancellationToken cancellationToken)
    {
        var label = $"{LabelKey}={verificationRun}";
        var removed = new List<string>();
        var failed = new List<ContainerCleanupFailure>();
        using var client = new DockerEngineClient(handler);
        IReadOnlyList<string> owned;
        try
        {
            owned = await client.ListByLabelAsync(label, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Every failure is recorded, including the cleanup's own deadline: the evidence entry is
            // written after this returns, and a cleanup that threw would lose it.
            failed.Add(new ContainerCleanupFailure(null, exception.Message));
            return new ContainerCleanupRecord(label, removed, failed);
        }

        foreach (var id in owned)
        {
            try
            {
                await client.RemoveAsync(id, cancellationToken).ConfigureAwait(false);
                removed.Add(id);
            }
            catch (Exception exception)
            {
                failed.Add(new ContainerCleanupFailure(id, exception.Message));
            }
        }

        return new ContainerCleanupRecord(label, removed, failed);
    }
}
