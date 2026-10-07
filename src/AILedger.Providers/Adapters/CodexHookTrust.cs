using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using AILedger.Providers.Navigation;
using AILedger.Providers.Process;

namespace AILedger.Providers.Adapters;

internal static class CodexHookTrust
{
    internal const string GuardMatcher = "^(Bash|PowerShell|Grep|grep|exec_command|shell|shell_command)$";

    internal static async Task TrustAsync(string executable, string governedHome, string workingDirectory,
        string expectedCommand, CancellationToken cancellationToken)
    {
        // Codex resolves CODEX_HOME symlinks before reporting hook sources (including
        // macOS /var -> /private/var). Compare and launch using the same physical paths.
        governedHome = RoslynSolutionPaths.Canonicalize(governedHome);
        workingDirectory = RoslynSolutionPaths.Canonicalize(workingDirectory);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        using var process = new System.Diagnostics.Process
        {
            StartInfo = StartInfo(executable, governedHome, workingDirectory)
        };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start Codex to validate navigation hook trust.");
        }

        var initialized = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdout = SystemProcessRunner.DrainAsync(process.StandardOutput, (line, _) =>
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var id) && id.TryGetInt32(out var number))
            {
                var response = number == 1 ? initialized : number == 2 ? listed : null;
                response?.TrySetResult(document.RootElement.Clone());
            }

            return ValueTask.CompletedTask;
        }, null, deadline.Token, ProviderOutputLimits.MaximumCharactersPerStream);
        var stderr = SystemProcessRunner.DrainAsync(process.StandardError,
            (_, _) => ValueTask.CompletedTask, null, deadline.Token);
        var exit = process.WaitForExitAsync(deadline.Token);
        Task[] lifetime = [stdout, stderr, exit];
        try
        {
            await SendAsync(process, new
            {
                id = 1, method = "initialize", @params = new
                {
                    clientInfo = new { name = "ailedger-hook-trust", version = "1" },
                    capabilities = new { experimentalApi = true }
                }
            }, deadline.Token).ConfigureAwait(false);
            var initialization = await ReadResponseAsync(initialized.Task, lifetime, deadline.Token)
                .ConfigureAwait(false);
            RequireResult(initialization);
            await SendAsync(process, new { method = "initialized", @params = new { } }, deadline.Token)
                .ConfigureAwait(false);
            await SendAsync(process, new
            {
                id = 2, method = "hooks/list", @params = new { cwds = new[] { workingDirectory } }
            }, deadline.Token).ConfigureAwait(false);
            var response = await ReadResponseAsync(listed.Task, lifetime, deadline.Token).ConfigureAwait(false);
            var configuration = ValidateResponse(response, governedHome, workingDirectory, expectedCommand);
            await File.AppendAllTextAsync(Path.Combine(governedHome, "config.toml"), configuration,
                deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Codex navigation hook trust initialization exceeded 25 seconds.", exception);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            var failure = await SystemProcessRunner.CleanupProcessAsync(new SystemProcessCleanupTarget(process),
                lifetime, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (failure is not null)
            {
                throw new InvalidOperationException("Could not stop Codex hook trust initialization.", failure);
            }
        }
    }

    internal static string ValidateResponse(JsonElement response, string governedHome, string workingDirectory,
        string expectedCommand)
    {
        try
        {
            return ValidateHooks(response, governedHome, workingDirectory, expectedCommand);
        }
        catch (InvalidOperationException exception)
        {
            var diagnostic = DiscoveryDiagnostic(response, governedHome, workingDirectory, expectedCommand);
            throw new InvalidOperationException(exception.Message + " Discovery response: " + diagnostic, exception);
        }
    }

    private static string ValidateHooks(JsonElement response, string governedHome, string workingDirectory,
        string expectedCommand)
    {
        var result = RequireResult(response);
        if (!result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array ||
            data.GetArrayLength() != 1)
        {
            throw InvalidHooks("Expected exactly one discovery entry.");
        }

        var entry = data[0];
        if (Text(entry, "cwd") != workingDirectory ||
            !entry.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array ||
            errors.GetArrayLength() != 0 ||
            !entry.TryGetProperty("hooks", out var hooks) || hooks.ValueKind != JsonValueKind.Array ||
            hooks.GetArrayLength() != 1)
        {
            throw InvalidHooks("Expected matching cwd, no discovery errors and exactly one hook.");
        }

        var hook = hooks[0];
        var source = Text(hook, "sourcePath");
        var key = Text(hook, "key");
        var hash = Text(hook, "currentHash");
        if (Text(hook, "handlerType") != "command" || Text(hook, "eventName") != "preToolUse" ||
            Text(hook, "matcher") != GuardMatcher ||
            Text(hook, "command") != expectedCommand || Text(hook, "source") != "user" ||
            !hook.TryGetProperty("async", out var asynchronous) || asynchronous.ValueKind != JsonValueKind.False ||
            !hook.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.True ||
            !hook.TryGetProperty("isManaged", out var managed) || managed.ValueKind != JsonValueKind.False ||
            (source != Path.Combine(governedHome, "hooks.json") && source != Path.Combine(governedHome, "config.toml")) ||
            key is null || !key.StartsWith(source + ":pre_tool_use:", StringComparison.Ordinal) ||
            hash is null || !hash.StartsWith("sha256:", StringComparison.Ordinal) || hash.Length != 71 ||
            !hash[7..].All(Uri.IsHexDigit))
        {
            throw InvalidHooks("Generated hook metadata did not match.");
        }

        return $"\n[hooks.state.{RoslynNavigation.Quote(key)}]\nenabled = true\ntrusted_hash = {RoslynNavigation.Quote(hash)}\n";
    }

    private static ProcessStartInfo StartInfo(string executable, string governedHome, string workingDirectory)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("app-server");
        start.Environment.Clear();
        foreach (var name in ProcessEnvironment.AmbientAllowlist)
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                start.Environment[name] = value;
            }
        }

        start.Environment["CODEX_HOME"] = governedHome;
        return start;
    }

    private static async Task SendAsync(System.Diagnostics.Process process, object message,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonElement> ReadResponseAsync(Task<JsonElement> response, Task[] lifetime,
        CancellationToken cancellationToken)
    {
        var completed = await Task.WhenAny(lifetime.Append(response)).WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (completed != response && !response.IsCompleted)
        {
            await completed.ConfigureAwait(false);
            throw new InvalidOperationException("Codex exited before returning navigation hook metadata.");
        }

        return await response.ConfigureAwait(false);
    }

    private static JsonElement RequireResult(JsonElement response)
    {
        if (response.TryGetProperty("error", out _) || !response.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Codex does not support the required navigation hook trust protocol.");
        }

        return result;
    }

    private static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static InvalidOperationException InvalidHooks(string reason) =>
        new("Codex hook discovery must contain only the expected generated navigation guard; launch refused. " + reason);

    private static string DiscoveryDiagnostic(JsonElement response, string home, string cwd, string command)
    {
        // Never persist arbitrary hook commands or error prose: ambient hooks can embed secrets.
        // Retain response structure, safe expected values, mismatches and hashes for correlation.
        var expected = new Dictionary<string, string>
        {
            ["cwd"] = cwd, ["command"] = command, ["matcher"] = GuardMatcher,
            ["handlerType"] = "command", ["eventName"] = "preToolUse", ["source"] = "user"
        };
        var remaining = 128;
        var sanitized = SanitizeDiscovery(response, "", expected, home, ref remaining, 0);
        var text = sanitized?.ToJsonString() ?? "null";
        if (text.Length > 4096) text = text[..4096] + " [truncated]";
        return text + "; response_sha256=" + Digest(response.GetRawText());
    }

    private static JsonNode? SanitizeDiscovery(JsonElement value, string field,
        IReadOnlyDictionary<string, string> expected, string home, ref int remaining, int depth)
    {
        if (--remaining < 0 || depth > 8) return JsonValue.Create("[truncated]");
        if (value.ValueKind == JsonValueKind.Object)
        {
            var result = new JsonObject();
            foreach (var property in value.EnumerateObject())
            {
                if (remaining < 0) break;
                if (!DiagnosticFields.Contains(property.Name, StringComparer.Ordinal)) continue;
                result[property.Name] = SanitizeDiscovery(property.Value, property.Name, expected, home, ref remaining, depth + 1);
            }
            return result;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var result = new JsonArray();
            foreach (var item in value.EnumerateArray())
            {
                if (remaining < 0) break;
                result.Add(SanitizeDiscovery(item, field, expected, home, ref remaining, depth + 1));
            }
            return result;
        }
        if (value.ValueKind != JsonValueKind.String) return JsonNode.Parse(value.GetRawText());
        var text = value.GetString()!;
        if (expected.TryGetValue(field, out var match) && text == match) return JsonValue.Create("[expected]");
        if (field == "sourcePath" && (text == Path.Combine(home, "hooks.json") || text == Path.Combine(home, "config.toml")))
            return JsonValue.Create("[expected generated source]");
        if (field == "key" && new[] { "hooks.json", "config.toml" }.Any(name =>
                text.StartsWith(Path.Combine(home, name) + ":pre_tool_use:", StringComparison.Ordinal)))
            return JsonValue.Create("[expected source prefix]");
        if (field == "currentHash" && text.StartsWith("sha256:", StringComparison.Ordinal) &&
            text.Length == 71 && text[7..].All(Uri.IsHexDigit))
            return JsonValue.Create("[valid sha256]");
        return JsonValue.Create($"[redacted; length={text.Length}; sha256={Digest(text)}]");
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static readonly string[] DiagnosticFields =
    [
        "id", "result", "error", "data", "cwd", "errors", "hooks", "key", "currentHash", "handlerType",
        "eventName", "matcher", "command", "source", "sourcePath", "enabled", "isManaged", "async", "code", "message"
    ];
}
