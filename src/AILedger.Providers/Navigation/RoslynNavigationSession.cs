using System.Text.Json.Nodes;
using AILedger.Providers.Adapters;

namespace AILedger.Providers.Navigation;

internal sealed class RoslynNavigationSession(
    RoslynSolutionPaths paths,
    Func<JsonObject, CancellationToken, Task<JsonObject>> exchange,
    RoslynFallbackStore? fallback = null)
{
    private readonly HashSet<string> loaded = new(RoslynSolutionPaths.Comparer);
    private string? active;

    internal async Task<JsonObject?> HandleAsync(JsonObject request, CancellationToken cancellationToken)
    {
        var method = request["method"]?.GetValue<string>();
        if (request["id"] is null)
        {
            if (method == "notifications/initialized")
            {
                await exchange(request, cancellationToken);
            }

            return null;
        }

        if (method == "tools/call")
        {
            return await CallToolAsync(request, cancellationToken);
        }

        if (method is not ("initialize" or "ping" or "tools/list"))
        {
            return new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone(),
                ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not supported." }
            };
        }

        var response = await exchange(request, cancellationToken);
        if (response["result"] is JsonObject result)
        {
            if (method == "initialize")
            {
                result["capabilities"] = new JsonObject { ["tools"] = new JsonObject() };
                result["instructions"] = RoslynNavigation.Guidance;
            }
            else if (method == "tools/list" && result["tools"] is JsonArray tools)
            {
                result["tools"] = FilterTools(tools);
                result.Remove("nextCursor");
            }
        }

        return response;
    }

    private async Task<JsonObject> CallToolAsync(JsonObject request, CancellationToken cancellationToken)
    {
        var name = request["params"]?["name"]?.GetValue<string>();
        if (name is null || !RoslynNavigation.Tools.Contains(name, StringComparer.Ordinal))
        {
            return ToolError(request, "Tool is not available through this navigation connection.");
        }

        string? scope = null;
        try
        {
            var selection = PrepareCall(request, name);
            ValidateQuery(request, name);
            scope = name == "list_solutions" ? null : Path.GetDirectoryName(selection ?? active!);
            if (scope is not null)
            {
                fallback?.Revoke(scope);
            }

            var response = await exchange(request, cancellationToken);
            if (selection is not null && Succeeded(response))
            {
                loaded.Add(selection);
                active = selection;
            }

            if (scope is not null && !Succeeded(response) && response["error"]?["code"]?.GetValue<int>() != -32602)
            {
                RecordFailure(response, scope, name);
            }

            return response;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            var response = ToolError(request, exception.Message);
            // Local path/argument refusals are not failures of an authorized Roslyn query.
            if (scope is not null && exception is not ArgumentException)
            {
                RecordFailure(response, scope, name);
            }

            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var response = ToolError(request, "Roslyn query timed out.");
            if (scope is not null)
            {
                RecordFailure(response, scope, name);
            }

            return response;
        }
    }

    private void RecordFailure(JsonObject response, string scope, string operation)
    {
        var reason = response["result"]?["content"]?.ToJsonString() ?? response["error"]?.ToJsonString() ?? "Roslyn failed";
        var id = fallback?.Record(scope, operation, reason);
        if (id is not null && response["result"]?["content"] is JsonArray content)
        {
            content.Add(new JsonObject { ["type"] = "text",
                ["text"] = $"Recorded Roslyn failure {id}: one CLI C# search within {scope} is permitted for 10 minutes. A successful retry revokes it." });
        }
    }

    private static void ValidateQuery(JsonObject request, string name)
    {
        var field = name switch
        {
            "search_symbols" => "query",
            "go_to_definition" or "find_references" or "find_callers" or "get_overloads" or "find_tests_for_symbol" => "symbol",
            _ => null
        };
        var arguments = request["params"]?["arguments"] as JsonObject;
        if (field is not null && (arguments?[field] is not JsonValue value ||
            !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)))
        {
            throw new ArgumentException($"'{field}' must be a nonempty string.");
        }

        if (name == "get_method_source" && (arguments?["symbols"] is not JsonArray symbols ||
            symbols.Count == 0 || symbols.Any(symbol => symbol is not JsonValue item ||
                !item.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))))
        {
            throw new ArgumentException("'symbols' must contain nonempty symbol names.");
        }

        foreach (var key in new[] { "limit", "maxDepth" })
        {
            if (key == "maxDepth" && arguments?.ContainsKey(key) == true && arguments[key] is null)
            {
                throw new ArgumentException("'maxDepth' must be an integer.");
            }

            if (arguments?[key] is { } number && (number is not JsonValue numericValue ||
                !numericValue.TryGetValue<int>(out _)))
            {
                throw new ArgumentException($"'{key}' must be an integer.");
            }
        }

        if (arguments?.ContainsKey("transitive") == true && (arguments["transitive"] is not JsonValue booleanValue ||
            !booleanValue.TryGetValue<bool>(out _)))
        {
            throw new ArgumentException("'transitive' must be a boolean.");
        }

        foreach (var key in new[] { "kinds", "include", "rootProjects" })
        {
            if (arguments?[key] is { } values && (values is not JsonArray strings ||
                strings.Any(item => item is not null && (item is not JsonValue stringValue ||
                    !stringValue.TryGetValue<string>(out _)))))
            {
                throw new ArgumentException($"'{key}' must be an array of strings.");
            }
        }
    }

    private string? PrepareCall(JsonObject request, string name)
    {
        if (name is "load_solution" or "set_active_solution")
        {
            // A failed selection must not silently query the preceding repository.
            active = null;
            var arguments = request["params"]!["arguments"] as JsonObject
                ?? throw new ArgumentException("Supply the absolute solution path in the tool arguments.");
            var field = name == "load_solution" ? "path" : "name";
            var value = arguments[field];
            if (value is not JsonValue pathValue || !pathValue.TryGetValue<string>(out var path))
            {
                throw new ArgumentException($"'{field}' must be an absolute solution path.");
            }

            var solution = paths.Resolve(path);
            if (name == "set_active_solution" && !loaded.Contains(solution))
            {
                throw new ArgumentException("Solution has not been loaded in this session; call load_solution first.");
            }

            arguments[field] = solution;
            if (name == "load_solution")
            {
                // Background loading would leave the previous repository active during indexing.
                arguments["background"] = false;
                loaded.Remove(solution);
            }

            return solution;
        }

        if (name != "list_solutions")
        {
            if (active is null)
            {
                throw new ArgumentException("No solution selected. Call load_solution or set_active_solution successfully first.");
            }

            paths.Resolve(active);
        }

        return null;
    }

    private static bool Succeeded(JsonObject response) => response["error"] is null &&
        response["result"] is JsonObject result && result["isError"]?.GetValue<bool>() != true;

    private static JsonArray FilterTools(JsonArray tools)
    {
        var filtered = new JsonArray();
        foreach (var tool in tools.OfType<JsonObject>())
        {
            if (!RoslynNavigation.Tools.Contains(tool["name"]?.GetValue<string>(), StringComparer.Ordinal))
            {
                continue;
            }

            var copy = (JsonObject)tool.DeepClone();
            if (copy["name"]?.GetValue<string>() == "set_active_solution")
            {
                copy["description"] = "Select an already loaded solution by its exact absolute path in 'name'.";
            }
            else if (copy["name"]?.GetValue<string>() == "load_solution")
            {
                copy["description"] = "Load and select a .sln/.slnx by absolute path inside this launch's authorized directories. Loading waits for completion.";
                if (copy["inputSchema"]?["properties"] is JsonObject properties)
                {
                    properties.Remove("background");
                }
            }

            filtered.Add(copy);
        }

        return filtered;
    }

    private static JsonObject ToolError(JsonObject request, string message) => new()
    {
        ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone(),
        ["result"] = new JsonObject
        {
            ["isError"] = true,
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = message })
        }
    };
}
