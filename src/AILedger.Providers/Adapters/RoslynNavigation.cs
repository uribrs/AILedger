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
        Required C# navigation: use Roslyn FIRST for symbol searches, definitions, references,
        callers, overloads and related tests. Do not substitute rg, grep, find or shell scripts
        for a C# semantic query. CLI file-name discovery to locate a .sln/.slnx is permitted.
        Call roslyn load_solution with its absolute path before semantic queries. Roslyn starts
        empty; the launch directory does not select a solution.
        When changing repositories, load_solution or set_active_solution with the exact solution
        path, then confirm the active path and skipped projects with list_solutions. Loading is
        restricted to this launch's navigation directories; never substitute AILedger's own solution.
        After edits or checkout changes, rebuild_solution before another semantic query. Keep
        queries and limits small; use transitive test discovery only when needed. find_callers
        takes Type.Method, not an overload signature. C# shell and native Grep searches are guarded:
        fallback requires a failure recorded by the bridge, not a self-declared exception. One
        failure permits one simple CLI search within the solution directory for ten minutes;
        another Roslyn attempt revokes it. Invalid arguments and rejected paths do not count.
        Empty results do not prove no dependencies, but do not unlock CLI fallback either.
        CLI remains appropriate for explicit non-C# file filters, targeted source reads,
        filename discovery, builds, tests and Git. Do not bypass the guard using custom scripts.
        """;

    internal static string Configuration(AgentLaunchRequest request, string launchDirectory)
    {
        var path = CreateSettingsFile(request, launchDirectory);
        return path is null ? string.Empty : Configuration(request.NavigationHostAssembly!, path, launchDirectory);
    }

    internal static string? CreateSettingsFile(AgentLaunchRequest request, string launchDirectory)
    {
        var executable = request.Environment.TryGetValue(ExecutableVariable, out var configured)
            ? configured
            : Environment.GetEnvironmentVariable(ExecutableVariable) ?? DefaultExecutable;
        if (request.NavigationHostAssembly is not { } host || !Path.IsPathFullyQualified(host) || !File.Exists(host))
        {
            return null;
        }

        var configurationPath = Path.Combine(launchDirectory, "roslyn-navigation.json");
        var settings = new RoslynNavigationSettings(executable,
            request.AdditionalDirectories.Prepend(request.WorkingDirectory)
                .Concat(request.NavigationDirectories ?? []).Distinct(StringComparer.Ordinal).ToArray(), request.LedgerRoot,
            Path.Combine(launchDirectory, "navigation-failures"));
        File.WriteAllText(configurationPath, JsonSerializer.Serialize(settings));
        return configurationPath;
    }

    internal const string HookMatcher = "^(Bash|PowerShell|Grep|grep|exec_command|shell|shell_command)$";

    internal static string GuardCommand(string host, string settingsPath) =>
        $"dotnet {ShellQuote(host)} navigation guard {ShellQuote(settingsPath)}";

    internal static object Hooks(string command) => new
    {
        PreToolUse = new[] { new { matcher = HookMatcher,
            hooks = new[] { new { type = "command", command, timeout = 30 } } } }
    };

    private static string ShellQuote(string value) => OperatingSystem.IsWindows()
        ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
        : "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

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
