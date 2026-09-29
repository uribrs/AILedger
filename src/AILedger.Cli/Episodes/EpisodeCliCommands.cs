using System.Text;
using AILedger.Core.Application;
using AILedger.Core.Domain;
using AILedger.Core.Episodes;
using AILedger.Core.Handoffs;
using AILedger.Providers.Episodes;
using AILedger.Providers.Process;
using AILedger.Storage;
using AILedger.Storage.Episodes;

namespace AILedger.Cli.Episodes;

// Explicit experimental entry point before the governed CLI's task/service construction.
// The operator selects a trusted host file; provider payloads never contain this configuration.
internal static class EpisodeCliCommands
{
    internal static async Task<int> RunAsync(string[] args, CancellationToken token)
    {
        try
        {
            if (args.Length < 2 || args.Contains("--help"))
            {
                await Console.Out.WriteLineAsync("episode run|resume --authority FILE --store DIR --scratch DIR --body-stdin\n" +
                    "episode inspect|reconcile --authority FILE --store DIR --request-id ID [--confirm-provider-stopped]\n" +
                    "Experimental offline read-only bridge; no kernel writes, model spend or acceptance.");
                return 0;
            }
            var options = Options(args.Skip(2).ToArray());
            var authoritySource = new FileAuthority(Required(options, "authority"));
            var authority = await authoritySource.ReadAsync(token).ConfigureAwait(false);
            var store = new FileEpisodeStore(Required(options, "store"));
            var reducer = new TaskReducer();
            var inspector = new FileGovernedTaskService(authority.LedgerRoot,
                new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
            var executor = new EpisodeExecutor(store, authoritySource, inspector, new SystemProcessRunner(),
                options.GetValueOrDefault("scratch") ?? Path.GetTempPath(), KernelVersion.Describe());
            EpisodeInspection result;
            if (args[1] is "run" or "resume")
            {
                if (!options.ContainsKey("body-stdin")) throw new ArgumentException("Supply --body-stdin with the episode-start v1 body.");
                var start = HandoffJson.ParseDocument<EpisodeStart>(await ReadBodyAsync(token).ConfigureAwait(false));
                result = await executor.ExecuteAsync(start, args[1] == "resume", token).ConfigureAwait(false);
            }
            else if (args[1] == "inspect")
                result = await executor.InspectAsync(EpisodeExecutionValidation.ExecutionId(authority, Required(options, "request-id")), token).ConfigureAwait(false);
            else if (args[1] == "reconcile")
                result = await executor.ReconcileAsync(Required(options, "request-id"), options.ContainsKey("confirm-provider-stopped"), token).ConfigureAwait(false);
            else throw new ArgumentException("Unknown episode command.");
            await Console.Out.WriteLineAsync(EpisodeExecutionValidation.Serialize(result)).ConfigureAwait(false);
            return args[1] is "inspect" or "reconcile" || result.ExecutionOutcome == "succeeded" ? 0 : 3;
        }
        catch (OperationCanceledException) { await Console.Error.WriteLineAsync("Cancelled; inspect the execution journal before retry."); return 130; }
        catch (Exception e) when (e is ArgumentException or IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { await Console.Error.WriteLineAsync("Episode unavailable: " + e.Message); return 2; }
    }

    private static Dictionary<string, string> Options(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var values = new[] { "authority", "store", "scratch", "request-id" };
        foreach (var key in args.Where(a => a.StartsWith("--", StringComparison.Ordinal)))
            if (!values.Contains(key[2..]) && key is not ("--body-stdin" or "--confirm-provider-stopped"))
                throw new ArgumentException("Unsupported episode option: " + key);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Expected episode option.");
            var key = args[i][2..];
            var value = values.Contains(key) ? ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.") : "true";
            if (!options.TryAdd(key, value)) throw new ArgumentException("Duplicate episode option.");
        }
        return options;
    }
    private static string Required(Dictionary<string, string> options, string key) =>
        options.GetValueOrDefault(key) ?? throw new ArgumentException("Missing --" + key);
    private static async Task<string> ReadBodyAsync(CancellationToken token)
    {
        var body = new StringBuilder(); var buffer = new char[8192]; int count;
        while ((count = await Console.In.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            body.Append(buffer, 0, count);
            if (body.Length > HandoffJson.MaximumRequestBytes) throw new ArgumentException("Episode start exceeds 512 KiB.");
        }
        return body.ToString();
    }
    private sealed class FileAuthority(string path) : IEpisodeAuthoritySource
    {
        public async Task<EpisodeAuthority> ReadAsync(CancellationToken token)
        {
            if (!Path.IsPathFullyQualified(path) || new FileInfo(path).LinkTarget is not null || new FileInfo(path).Length > 65536)
                throw new ArgumentException("Trusted authority must be a local absolute, non-symlink file under 64 KiB.");
            return HandoffJson.ParseDocument<EpisodeAuthority>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
        }
    }
}
