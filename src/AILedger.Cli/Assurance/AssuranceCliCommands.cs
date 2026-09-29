using System.Text;
using System.Text.Json;
using AILedger.Cli.Findings;
using AILedger.Core.Assurance;
using AILedger.Core.Episodes;

namespace AILedger.Cli.Assurance;

// Operator-owned setup/compatibility adapter. Agent calls use the typed MCP operations.
internal static class AssuranceCliCommands
{
    internal static async Task<int> RunAsync(string[] args, CancellationToken token)
    {
        try
        {
            if (args.Length < 2 || args.Contains("--help"))
            {
                await Console.Out.WriteLineAsync("assurance serve|inspect_assurance|read_assurance|run_assurance_checks|record_assurance|accept_assurance|reconcile --authority FILE --store DIR --principal ID --session ID [--body-stdin] [--request-id ID --check-principal ID --confirm-provider-stopped]\nLocal trusted operator setup only; no model launch, implementation execution or kernel completion. Protect authority and store from provider writes.");
                return 0;
            }
            var options = ParseOptions(args.Skip(2).ToArray());
            string Required(string name) => options.GetValueOrDefault(name) ?? throw new ArgumentException("Missing --" + name);
            var store = Required("store");
            var session = new AssuranceSession(Required("principal"), Required("session"), "external-client", null);
            var service = await AssuranceHost.OpenAsync(Required("authority"), store, session, null, token).ConfigureAwait(false);
            if (args[1] == "serve")
            {
                var host = new FindingsMcpHost(new(store, "assurance", session.Principal, session.SessionId,
                    Path.Combine(store, "assurance-telemetry")), null, _ => throw new InvalidOperationException("No governed binding on assurance-only host."), service, assuranceOnly: true);
                await new FindingsMcpServer(host, Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error).RunAsync(token).ConfigureAwait(false);
                return 0;
            }
            if (args[1] == "reconcile")
            {
                await service.ReconcileChecksAsync(Required("request-id"), Required("check-principal"), options.ContainsKey("confirm-provider-stopped"), token).ConfigureAwait(false);
                await Console.Out.WriteLineAsync("{\"outcome\":\"unknown\",\"reconciled\":true,\"acceptance\":\"not_assessed\"}"); return 0;
            }
            if (!AssuranceValidation.Tools.Contains(args[1]) || !options.ContainsKey("body-stdin")) throw new ArgumentException("Select a typed assurance operation and supply --body-stdin.");
            var body = new StringBuilder(); var buffer = new char[4096]; int count;
            while ((count = await Console.In.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
            {
                body.Append(buffer, 0, count);
                if (body.Length > 128 * 1024) throw new ArgumentException("Request exceeds 128 KiB.");
            }
            using var document = JsonDocument.Parse(body.ToString());
            var response = await service.InvokeAsync(args[1], document.RootElement, token).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(EpisodeExecutionValidation.Serialize(response)).ConfigureAwait(false);
            return response.Error is null ? 0 : 3;
        }
        catch (Exception e) when (e is ArgumentException or IOException or JsonException or UnauthorizedAccessException or AssuranceRefusal or OperationCanceledException)
        { await Console.Error.WriteLineAsync("Assurance unavailable: " + e.Message); return 2; }
    }
    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var values = new[] { "authority", "store", "principal", "session", "request-id", "check-principal" };
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Expected named operator option.");
            var key = args[i][2..];
            if (!values.Contains(key) && key is not ("body-stdin" or "confirm-provider-stopped")) throw new ArgumentException("Unsupported assurance option.");
            var value = values.Contains(key) ? ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.") : "true";
            if (!result.TryAdd(key, value)) throw new ArgumentException("Duplicate option.");
        }
        return result;
    }
}
