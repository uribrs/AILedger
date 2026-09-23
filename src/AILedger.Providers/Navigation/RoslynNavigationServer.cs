using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Providers.Process;
using SystemProcess = System.Diagnostics.Process;

namespace AILedger.Providers.Navigation;

public static class RoslynNavigationServer
{
    public static async Task<int> RunAsync(string configurationPath, CancellationToken cancellationToken)
    {
        RoslynNavigationSettings? settings = null;
        var initialized = false;
        try
        {
            var configuration = await File.ReadAllTextAsync(configurationPath, cancellationToken);
            settings = JsonSerializer.Deserialize<RoslynNavigationSettings>(configuration)
                ?? throw new ArgumentException("Missing Roslyn navigation configuration.");
            var paths = new RoslynSolutionPaths(settings);
            await using var backend = RoslynStdioBackend.Start(settings.Executable);
            var fallback = settings.GuardDirectory is { } directory ? new RoslynFallbackStore(directory) : null;
            var session = new RoslynNavigationSession(paths, backend.ExchangeAsync, fallback);
            while (await Console.In.ReadLineAsync(cancellationToken) is { } line)
            {
                var request = JsonNode.Parse(line) as JsonObject
                    ?? throw new ArgumentException("Expected a JSON-RPC object.");
                var response = await session.HandleAsync(request, cancellationToken);
                initialized |= request["method"]?.GetValue<string>() == "initialize" && response?["result"] is not null;
                if (response is not null)
                {
                    await Console.Out.WriteLineAsync(response.ToJsonString().AsMemory(), cancellationToken);
                    await Console.Out.FlushAsync(cancellationToken);
                }
            }

            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            if (!initialized && settings?.GuardDirectory is { } directory)
            {
                // Missing/broken optional dependencies still leave an observed startup failure.
                // Never include the ledger store as a source-navigation fallback directory.
                var fallback = new RoslynFallbackStore(directory);
                var ledger = RoslynSolutionPaths.Canonicalize(settings.LedgerRoot);
                foreach (var scope in settings.AllowedDirectories.Select(path => RoslynSolutionPaths.Canonicalize(path)).Distinct())
                {
                    if (!RoslynSolutionPaths.Contains(ledger, scope))
                    {
                        fallback.Record(scope, "startup", exception.Message);
                    }
                }
            }

            await Console.Error.WriteLineAsync($"Roslyn navigation stopped: {exception.Message}. Any recorded fallback is limited to one search per directory.");
            return 1;
        }
    }
}

internal sealed class RoslynStdioBackend : IAsyncDisposable
{
    private readonly SystemProcess process;
    private readonly string directory;
    private readonly Task drainErrors;
    private readonly CancellationTokenSource drainCancellation = new();
    private readonly TimeSpan exchangeTimeout;
    private Task? stop;
    private bool unavailable;

    private RoslynStdioBackend(SystemProcess process, string directory, TimeSpan exchangeTimeout)
    {
        this.process = process;
        this.directory = directory;
        this.exchangeTimeout = exchangeTimeout;
        drainErrors = DrainErrorsAsync(process.StandardError, drainCancellation.Token);
    }

    internal static RoslynStdioBackend Start(string executable, TimeSpan? exchangeTimeout = null)
    {
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
        {
            throw new FileNotFoundException("The configured Roslyn executable is unavailable; install Roslyn or correct its absolute path.");
        }

        var directory = Directory.CreateTempSubdirectory("ailedger-roslyn-").FullName;
        var process = new SystemProcess
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = directory,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            }
        };
        try
        {
            process.Start();
            return new RoslynStdioBackend(process, directory, exchangeTimeout ?? TimeSpan.FromSeconds(55));
        }
        catch
        {
            process.Dispose();
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    internal async Task<JsonObject> ExchangeAsync(JsonObject request, CancellationToken cancellationToken)
    {
        if (unavailable)
        {
            throw new IOException("Roslyn connection stopped after a previous transport failure; start a new navigation session.");
        }

        try
        {
            return await ExchangeCoreAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or JsonException)
        {
            // Once a request loses its response, neither the protocol stream nor the backend's
            // active solution is trustworthy. Stop it before returning a recoverable tool error.
            unavailable = true;
            await StopAsync();
            if (exception is JsonException)
            {
                throw new IOException("Invalid Roslyn response.", exception);
            }

            throw;
        }
    }

    private async Task<JsonObject> ExchangeCoreAsync(JsonObject request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(exchangeTimeout);
        await process.StandardInput.WriteLineAsync(request.ToJsonString().AsMemory(), timeout.Token);
        await process.StandardInput.FlushAsync(timeout.Token);
        if (request["id"] is not { } id)
        {
            return new JsonObject();
        }

        while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
        {
            var response = JsonNode.Parse(line) as JsonObject
                ?? throw new IOException("Invalid Roslyn response");
            if (JsonNode.DeepEquals(response["id"], id))
            {
                return response;
            }

            // Upstream log/progress notifications need no reply. Server requests are unsupported.
            if (response["id"] is not null)
            {
                throw new IOException("Unexpected Roslyn request or response identifier");
            }
        }

        throw new IOException("Roslyn process exited before responding");
    }

    public async ValueTask DisposeAsync()
    {
        unavailable = true;
        try
        {
            await StopAsync();
        }
        finally
        {
            drainCancellation.Dispose();
            process.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private Task StopAsync() => stop ??= StopCoreAsync();

    private async Task StopCoreAsync()
    {
        // An exited backend may leave a descendant holding stderr open. Bound both the
        // reap and I/O cleanup instead of waiting indefinitely for that descendant's EOF.
        await drainCancellation.CancelAsync();
        var failure = await SystemProcessRunner.CleanupProcessAsync(new SystemProcessCleanupTarget(process),
            [drainErrors], TimeSpan.FromSeconds(5));
        if (failure is not null)
        {
            throw new IOException("Could not stop the failed Roslyn connection.", failure);
        }
    }

    private static async Task DrainErrorsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) != 0)
        {
            // Do not retain MSBuild's potentially unbounded diagnostic output in agent context.
        }
    }
}
