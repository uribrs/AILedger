using System.Text.Json;
using System.Text.Json.Nodes;

namespace AILedger.Providers.Navigation;

public static class RoslynSearchGuard
{
    public static async Task<int> RunAsync(string configurationPath, CancellationToken cancellationToken)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<RoslynNavigationSettings>(
                await File.ReadAllTextAsync(configurationPath, cancellationToken))
                ?? throw new ArgumentException("Missing navigation configuration.");
            var hook = JsonNode.Parse(await Console.In.ReadToEndAsync(cancellationToken)) as JsonObject
                ?? throw new ArgumentException("Expected hook input.");
            var denial = Evaluate(settings, hook);
            if (denial is not null)
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    hookSpecificOutput = new { hookEventName = "PreToolUse", permissionDecision = "deny", permissionDecisionReason = denial }
                }));
            }

            // Empty success leaves the provider's normal permission/sandbox checks intact.
            return 0;
        }
        catch (Exception exception)
        {
            // Both providers treat exit 2 as denial. Other errors may fail open.
            await Console.Error.WriteLineAsync($"C# search guard could not validate this call: {exception.Message}");
            return 2;
        }
    }

    internal static string? Evaluate(RoslynNavigationSettings settings, JsonObject hook)
    {
        var search = CSharpSearchClassifier.Classify(hook);
        if (search is null) { return null; }
        if (search.CanUseFallback && settings.GuardDirectory is { } directory &&
            new RoslynFallbackStore(directory).Consume(search.Targets) is not null)
        {
            return null;
        }

        return "C# search requires Roslyn. Load the target repository's solution and use search_symbols, " +
            "go_to_definition, find_references or find_callers. Shell/Grep C# searches are permitted only " +
            "after a recorded Roslyn failure, once within that solution directory. Use a single search " +
            "command with explicit paths for fallback. File-name discovery, targeted source reads and " +
            "explicit non-C# file filters remain available.";
    }
}
