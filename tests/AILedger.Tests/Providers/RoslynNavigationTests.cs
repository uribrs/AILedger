using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Providers.Navigation;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

public sealed class RoslynNavigationTests
{
    [Fact]
    public void TomlPathsPreserveUnicodeAndEscapeDelimitersAndControls()
    {
        Assert.Equal("\"C:\\\\code\\\"😀\\u000A\"", RoslynNavigation.Quote("C:\\code\"😀\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative-command")]
    [InlineData("/does-not-exist/roslyn")]
    public void WithoutCliHostDoesNotConfigureServer(string executable)
    {
        var request = ProviderProtocolTests.Request("codex", AgentLaunchMode.New, null) with
        {
            Environment = new Dictionary<string, string> { [RoslynNavigation.ExecutableVariable] = executable }
        };
        Assert.Empty(RoslynNavigation.Configuration(request, Path.GetTempPath()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdapterConfiguresOnlyNavigationAndRetainsReviewerIsolation(bool reviewer)
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, ".git"));
        var executable = Path.Combine(root.Path, "roslyn \"quoted\" command");
        File.WriteAllText(executable, string.Empty);
        // No solution in the working directory: task-wide runs still get on-demand navigation.
        var repository = Directory.CreateDirectory(Path.Combine(root.Path, "other-repo")).FullName;
        string? home = null;
        var runner = new ScriptedProcessRunner()
            .Enqueue(0, ["codex-cli 1.2.3"])
            .Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"])
            .Enqueue(0, ["SESSION_ID --json"])
            .Enqueue(invocation =>
            {
                home = invocation.Environment["CODEX_HOME"];
                var config = File.ReadAllText(Path.Combine(home, "config.toml"));
                AssertCodexHooks(home, config, root.Path);
                Assert.Contains("command = \"dotnet\"", config, StringComparison.Ordinal);
                Assert.Contains("\"navigation\", \"serve\"", config, StringComparison.Ordinal);
                var settings = JsonSerializer.Deserialize<RoslynNavigationSettings>(
                    File.ReadAllText(Path.Combine(home, "roslyn-navigation.json")))!;
                Assert.Equal(executable, settings.Executable);
                Assert.Contains(root.Path, settings.AllowedDirectories);
                Assert.Contains(repository, settings.AllowedDirectories);
                Assert.Contains($"enabled_tools = {JsonSerializer.Serialize(RoslynNavigation.Tools)}", config, StringComparison.Ordinal);
                Assert.Contains("required = false", config, StringComparison.Ordinal);
                Assert.Contains("default_tools_approval_mode = \"approve\"", config, StringComparison.Ordinal);
                Assert.DoesNotContain("rename_symbol", config, StringComparison.Ordinal);
                Assert.Contains("load_solution", config, StringComparison.Ordinal);
                Assert.Contains("rebuild_solution before another semantic query", invocation.StandardInput, StringComparison.Ordinal);
                Assert.Contains("Required C# navigation: use Roslyn FIRST", invocation.StandardInput, StringComparison.Ordinal);
                Assert.Equal(reviewer, invocation.Arguments.Contains("project_doc_max_bytes=0"));
                return new ScriptedProcessResult(0,
                    ["{\"type\":\"thread.started\",\"thread_id\":\"navigation-smoke\"}", "{\"type\":\"turn.completed\"}"], []);
            });
        var request = ProviderProtocolTests.Request("codex", AgentLaunchMode.New, null) with
        {
            WorkingDirectory = root.Path,
            AdditionalDirectories = [repository],
            NavigationHostAssembly = typeof(CliApplication).Assembly.Location,
            Environment = new Dictionary<string, string> { [RoslynNavigation.ExecutableVariable] = executable },
            Assurance = reviewer ? new AssuranceBinding(1, [new WorkItemId("W1")],
                [new(new WorkItemId("W1"), new RunId("WORK1"))], new string('a', 64), new RunId("VERIFY1")) : null
        };

        var result = await new CodexAgentAdapter(runner, (_, _, _, _, _) => Task.CompletedTask)
            .RunAsync(request, CancellationToken.None);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.NotNull(home);
        Assert.False(Directory.Exists(home));
    }

    [Theory]
    [InlineData("codex", "worker", false)]
    [InlineData("codex", "verifier", false)]
    [InlineData("codex", "reviewer", false)]
    [InlineData("claude", "worker", false)]
    [InlineData("claude", "verifier", false)]
    [InlineData("claude", "reviewer", false)]
    [InlineData("codex", "worker", true)]
    [InlineData("claude", "worker", true)]
    public async Task ScopedProvidersNavigateRepositoryWithoutWideningWriteDirectories(
        string provider, string role, bool missingExecutable)
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, ".git"));
        var project = Directory.CreateDirectory(Path.Combine(root.Path, "src", "App")).FullName;
        var executable = missingExecutable ? string.Empty : Path.Combine(root.Path, "roslyn");
        if (!missingExecutable) File.WriteAllText(executable, string.Empty);
        File.WriteAllText(Path.Combine(root.Path, "App.slnx"), "<Solution />");
        var reviewer = role == "reviewer";
        var request = ProviderProtocolTests.Request(provider, AgentLaunchMode.New, null) with
        {
            WorkingDirectory = project,
            AdditionalDirectories = [],
            NavigationDirectories = [root.Path],
            NavigationHostAssembly = typeof(CliApplication).Assembly.Location,
            Environment = new Dictionary<string, string> { [RoslynNavigation.ExecutableVariable] = executable },
            Assurance = role == "worker" ? null : new AssuranceBinding(1, [new WorkItemId("W1")],
                [new(new WorkItemId("W1"), new RunId("WORK1"))], new string('a', 64),
                reviewer ? new RunId("VERIFY1") : null)
        };
        var runner = new ScriptedProcessRunner();
        if (provider == "codex")
        {
            runner.Enqueue(0, ["codex-cli 1.2.3"])
                .Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"])
                .Enqueue(0, ["SESSION_ID --json"]);
        }
        else
        {
            runner.Enqueue(0, ["2.0.0 (Claude Code)"])
                .Enqueue(0, ["--print --output-format --session-id --resume --permission-prompts --settings --strict-mcp-config --disable-slash-commands"]);
        }

        string? scratchDirectory = null;
        runner.Enqueue(invocation =>
        {
            Assert.Equal(project, invocation.WorkingDirectory);
            Assert.DoesNotContain("--add-dir", invocation.Arguments);
            Assert.DoesNotContain(root.Path, invocation.Arguments);
            string settingsPath;
            string brief;
            if (provider == "codex")
            {
                scratchDirectory = invocation.Environment["CODEX_HOME"];
                var configuration = File.ReadAllText(Path.Combine(scratchDirectory, "config.toml"));
                AssertCodexHooks(scratchDirectory, configuration, root.Path);
                Assert.Contains($"enabled_tools = {JsonSerializer.Serialize(NavigationTools)}", configuration, StringComparison.Ordinal);
                Assert.Contains("default_tools_approval_mode = \"approve\"", configuration, StringComparison.Ordinal);
                Assert.Contains("required = false", configuration, StringComparison.Ordinal);
                Assert.Equal(project, ProviderProtocolTests.ValueAfter(invocation.Arguments, "--cd"));
                Assert.Equal("workspace-write", ProviderProtocolTests.ValueAfter(invocation.Arguments, "--sandbox"));
                Assert.Equal(reviewer, invocation.Arguments.Contains("project_doc_max_bytes=0"));
                settingsPath = Path.Combine(scratchDirectory, "roslyn-navigation.json");
                brief = invocation.StandardInput;
            }
            else
            {
                var configurationPath = ProviderProtocolTests.ValueAfter(invocation.Arguments, "--mcp-config");
                scratchDirectory = Path.GetDirectoryName(configurationPath)!;
                using var configuration = JsonDocument.Parse(File.ReadAllText(configurationPath));
                var servers = configuration.RootElement.GetProperty("mcpServers");
                Assert.Single(servers.EnumerateObject());
                var server = servers.GetProperty("roslyn");
                Assert.Equal("stdio", server.GetProperty("type").GetString());
                Assert.Equal("dotnet", server.GetProperty("command").GetString());
                var arguments = server.GetProperty("args").EnumerateArray().Select(value => value.GetString()).ToArray();
                Assert.Equal(request.NavigationHostAssembly, arguments[0]);
                Assert.Equal("navigation", arguments[1]);
                Assert.Equal("serve", arguments[2]);
                settingsPath = arguments[3]!;
                Assert.Equal(NavigationTools.Select(tool => $"mcp__roslyn__{tool}"),
                    ProviderProtocolTests.ValueAfter(invocation.Arguments, "--allowedTools").Split(','));
                Assert.Contains("--strict-mcp-config", invocation.Arguments);
                Assert.Contains("--disable-slash-commands", invocation.Arguments);
                using var providerSettings = JsonDocument.Parse(ProviderProtocolTests.ValueAfter(invocation.Arguments, "--settings"));
                Assert.False(providerSettings.RootElement.GetProperty("disableAllHooks").GetBoolean());
                AssertNavigationHook(providerSettings.RootElement.GetProperty("hooks"));
                Assert.True(providerSettings.RootElement.GetProperty("sandbox").GetProperty("enabled").GetBoolean());
                Assert.Equal(reviewer, providerSettings.RootElement.TryGetProperty("env", out var environment));
                if (reviewer)
                {
                    foreach (var key in new[] { "CLAUDE_CODE_DISABLE_CLAUDE_MDS", "CLAUDE_CODE_DISABLE_AUTO_MEMORY", "CLAUDE_CODE_DISABLE_ORG_MEMORY" })
                    {
                        Assert.Equal("1", environment.GetProperty(key).GetString());
                        Assert.Equal("1", invocation.Environment[key]);
                    }
                    Assert.Equal("0", environment.GetProperty("CLAUDE_CODE_POST_TURN_MEMORY").GetString());
                    Assert.Equal("0", invocation.Environment["CLAUDE_CODE_POST_TURN_MEMORY"]);
                }
                brief = ProviderProtocolTests.ValueAfter(invocation.Arguments, "-p");
            }

            var settings = JsonSerializer.Deserialize<RoslynNavigationSettings>(File.ReadAllText(settingsPath))!;
            Assert.Equal(executable, settings.Executable);
            Assert.Equal(new[] { project, root.Path }, settings.AllowedDirectories);
            Assert.Equal(request.LedgerRoot, settings.LedgerRoot);
            Assert.Contains("Required C# navigation: use Roslyn FIRST", brief, StringComparison.Ordinal);
            if (reviewer) Assert.Contains("neutral manifest", brief, StringComparison.Ordinal);
            return provider == "codex"
                ? new ScriptedProcessResult(0,
                    ["{\"type\":\"thread.started\",\"thread_id\":\"scoped-navigation\"}", "{\"type\":\"turn.completed\"}"], [])
                : new ScriptedProcessResult(0,
                    [$"{{\"type\":\"result\",\"session_id\":\"{ProviderProtocolTests.ValueAfter(invocation.Arguments, "--session-id")}\",\"result\":\"done\"}}"], []);
        });

        IAgentAdapter adapter = provider == "codex"
            ? new CodexAgentAdapter(runner, (_, _, _, _, _) => Task.CompletedTask) : new ClaudeAgentAdapter(runner);
        var result = await adapter.RunAsync(request, CancellationToken.None);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.NotNull(scratchDirectory);
        Assert.False(Directory.Exists(scratchDirectory));
    }

    private static void AssertCodexHooks(string home, string configuration, string repository)
    {
        Assert.Contains("hooks = true", configuration, StringComparison.Ordinal);
        Assert.Contains("plugins = false", configuration, StringComparison.Ordinal);
        var canonicalRepository = RoslynSolutionPaths.Canonicalize(repository);
        Assert.Contains($"[projects.{RoslynNavigation.Quote(canonicalRepository)}]", configuration, StringComparison.Ordinal);
        Assert.Contains("trust_level = \"untrusted\"", configuration, StringComparison.Ordinal);
        using var hooks = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, "hooks.json")));
        AssertNavigationHook(hooks.RootElement.GetProperty("hooks"));
    }

    private static void AssertNavigationHook(JsonElement hooks)
    {
        var rule = Assert.Single(hooks.GetProperty("PreToolUse").EnumerateArray());
        Assert.Contains("Bash", rule.GetProperty("matcher").GetString(), StringComparison.Ordinal);
        var action = Assert.Single(rule.GetProperty("hooks").EnumerateArray());
        Assert.Equal("command", action.GetProperty("type").GetString());
        Assert.Contains("navigation guard", action.GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.Contains("roslyn-navigation.json", action.GetProperty("command").GetString(), StringComparison.Ordinal);
    }

    private static readonly string[] NavigationTools =
    [
        "list_solutions", "search_symbols", "go_to_definition", "find_references", "find_callers",
        "get_overloads", "get_method_source", "find_tests_for_symbol", "rebuild_solution", "load_solution",
        "set_active_solution"
    ];

}
