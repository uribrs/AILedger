using System.Text.Json;

namespace AILedger.Cli.Findings;

internal static class FindingsStdioCommand
{
    internal static async Task<int> RunAsync(string configurationPath, CancellationToken cancellationToken)
    {
        try
        {
            var host = await FindingsMcpHost.LoadAsync(configurationPath, cancellationToken).ConfigureAwait(false);
            var server = new FindingsMcpServer(host, Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error);
            await server.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Before trustworthy configuration exists, stderr is the only safe diagnostic destination.
            await Console.Error.WriteLineAsync(JsonSerializer.Serialize(new
            {
                transport_attempt_id = Guid.NewGuid().ToString("N"), boundary = "host",
                transport_failure = "startup_failed", exception_type = e.GetType().Name,
                commit_state = "unknown", collection_status = "stderr_only"
            })).ConfigureAwait(false);
            return 1;
        }
    }
}
