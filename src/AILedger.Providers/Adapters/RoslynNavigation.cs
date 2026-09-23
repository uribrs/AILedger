using System.Text.Json;
using System.Text;
using AILedger.Core.Contracts;

namespace AILedger.Providers.Adapters;

internal static class RoslynNavigation
{
    internal const string Version = "2.18.1";
    internal const string ExecutableVariable = "AILEDGER_ROSLYN_EXECUTABLE";

    internal static readonly string[] Tools =
    [
        "list_solutions", "search_symbols", "go_to_definition", "find_references",
        "find_callers", "get_overloads", "get_method_source", "find_tests_for_symbol",
        "rebuild_solution"
    ];

    internal const string Guidance = """
        Code navigation: for C# prefer the roslyn MCP tools when available; use CLI search and
        targeted file reads for other languages or unavailable, empty, or suspect semantic results.
        Check list_solutions for skipped projects before relying on results. After source edits or
        checkout changes, rebuild_solution before another semantic query. Keep queries narrow and
        limits small; use transitive test discovery only when needed. find_callers takes Type.Method,
        not an overload signature; use get_overloads to inspect overloads. Empty results do not prove
        no dependencies. Replace searches with these calls rather than routinely doing both.
        """;

    internal static string Configuration(AgentLaunchRequest request)
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

        var solution = FindSolution(request.WorkingDirectory);
        return solution is null ? string.Empty : Configuration(executable, solution);
    }

    private static string DefaultExecutable => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".ailedger", "tools", "roslyn", Version,
        OperatingSystem.IsWindows() ? "roslyn-codelens-mcp.exe" : "roslyn-codelens-mcp");

    internal static string? FindSolution(string workingDirectory)
    {
        try
        {
            for (var directory = new DirectoryInfo(workingDirectory); directory is not null; directory = directory.Parent)
            {
                var solutions = directory.EnumerateFiles()
                    .Where(file => file.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
                                   file.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
                    .Take(2).ToArray();
                if (solutions.Length > 0)
                {
                    return solutions.Length == 1 &&
                           File.ReadAllText(solutions[0].FullName).Contains(".csproj", StringComparison.OrdinalIgnoreCase)
                        ? solutions[0].FullName
                        : null;
                }

                var git = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git))
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Optional navigation must not prevent a CLI-only launch.
        }

        return null;
    }

    internal static string Configuration(string executable, string solution) => $"""

        mcp_optional_startup_grace_ms = 0

        [mcp_servers.roslyn]
        command = {Quote(executable)}
        args = [{Quote(solution)}]
        cwd = {Quote(Path.GetDirectoryName(solution)!)}
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
