using System.Text.Json;
using System.Text;
using AILedger.Core.Contracts;
using AILedger.Providers.Navigation;

namespace AILedger.Providers.Adapters;

internal static class RoslynNavigation
{
    internal const string Version = "2.18.1";
    internal const string ExecutableVariable = "AILEDGER_ROSLYN_EXECUTABLE";

    internal static readonly string[] Tools =
    [
        "list_solutions", "search_symbols", "go_to_definition", "find_references",
        "find_callers", "get_overloads", "get_method_source", "find_tests_for_symbol",
        "rebuild_solution", "load_solution", "set_active_solution"
    ];

    internal const string Guidance = """
        Code navigation: identify the target repository and language with a narrow CLI search first.
        For C#, locate its .sln/.slnx and call roslyn load_solution with its absolute path before
        semantic queries. Roslyn starts empty; the launch directory does not select a solution.
        When changing repositories, load_solution or set_active_solution with the exact solution
        path, then confirm the active path and skipped projects with list_solutions. Loading is
        restricted to this launch's granted directories; never substitute AILedger's own solution.
        After edits or checkout changes, rebuild_solution before another semantic query. Keep
        queries and limits small; use transitive test discovery only when needed. find_callers
        takes Type.Method, not an overload signature. For other languages, missing solutions,
        unavailable Roslyn, or empty/suspect results, use CLI search and targeted file reads.
        Empty results do not prove no dependencies. Replace searches rather than doing both.
        """;

    internal static string Configuration(AgentLaunchRequest request, string launchDirectory)
    {
        var executable = request.Environment.TryGetValue(ExecutableVariable, out var configured)
            ? configured
            : Environment.GetEnvironmentVariable(ExecutableVariable) ?? DefaultExecutable;
        // An empty explicit value disables navigation, including for a single launch.
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable) ||
            !File.Exists(executable))
        {
            return string.Empty;
        }

        if (request.NavigationHostAssembly is not { } host || !Path.IsPathFullyQualified(host) || !File.Exists(host))
        {
            return string.Empty;
        }

        var configurationPath = Path.Combine(launchDirectory, "roslyn-navigation.json");
        var settings = new RoslynNavigationSettings(executable,
            request.AdditionalDirectories.Prepend(request.WorkingDirectory).ToArray(), request.LedgerRoot);
        File.WriteAllText(configurationPath, JsonSerializer.Serialize(settings));
        return Configuration(host, configurationPath, launchDirectory);
    }

    private static string DefaultExecutable => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".ailedger", "tools", "roslyn", Version,
        OperatingSystem.IsWindows() ? "roslyn-codelens-mcp.exe" : "roslyn-codelens-mcp");

    internal static string Configuration(string host, string settingsPath, string workingDirectory) => $"""

        mcp_optional_startup_grace_ms = 0

        [mcp_servers.roslyn]
        command = "dotnet"
        args = [{Quote(host)}, "navigation", "serve", {Quote(settingsPath)}]
        cwd = {Quote(workingDirectory)}
        enabled_tools = {JsonSerializer.Serialize(Tools)}
        default_tools_approval_mode = "approve"
        startup_timeout_sec = 30
        tool_timeout_sec = 60
        required = false
        """ + Environment.NewLine;

    // JSON surrogate-pair escapes are not valid TOML Unicode scalars. Preserve Unicode and
    // escape only TOML's string delimiters and control characters, including Windows slashes.
    internal static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        foreach (var character in value)
        {
            result.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                < ' ' or '\u007f' => $"\\u{(int)character:X4}",
                _ => character.ToString()
            });
        }

        return result.Append('"').ToString();
    }
}
