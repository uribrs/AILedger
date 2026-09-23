using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SystemProcess = System.Diagnostics.Process;

namespace AILedger.Providers.Navigation;

public static class RoslynNavigationServer
{
    public static async Task<int> RunAsync(string configurationPath, CancellationToken cancellationToken)
    {
        try
        {
            var configuration = await File.ReadAllTextAsync(configurationPath, cancellationToken);
            var settings = JsonSerializer.Deserialize<RoslynNavigationSettings>(configuration)
                ?? throw new ArgumentException("Missing Roslyn navigation configuration.");
            var paths = new RoslynSolutionPaths(settings);
            await using var backend = RoslynStdioBackend.Start(settings.Executable);
            var session = new RoslynNavigationSession(paths, backend.ExchangeAsync);
            while (await Console.In.ReadLineAsync(cancellationToken) is { } line)
            {
                var request = JsonNode.Parse(line) as JsonObject
                    ?? throw new ArgumentException("Expected a JSON-RPC object.");
                var response = await session.HandleAsync(request, cancellationToken);
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
            await Console.Error.WriteLineAsync($"Roslyn navigation stopped: {exception.Message}. Use CLI navigation.");
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

    private RoslynStdioBackend(SystemProcess process, string directory)
    {
        this.process = process;
        this.directory = directory;
        drainErrors = DrainErrorsAsync(process.StandardError, drainCancellation.Token);
    }

    internal static RoslynStdioBackend Start(string executable)
    {
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
            return new RoslynStdioBackend(process, directory);
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(55));
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
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        await process.WaitForExitAsync();
        // A backend that has already exited may leave a descendant holding stderr open.
        // Waiting for EOF in that case would strand the optional MCP connection indefinitely.
        await drainCancellation.CancelAsync();
        try
        {
            await drainErrors;
        }
        catch (OperationCanceledException) when (drainCancellation.IsCancellationRequested)
        {
        }

        drainCancellation.Dispose();
        process.Dispose();
        Directory.Delete(directory, recursive: true);
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
