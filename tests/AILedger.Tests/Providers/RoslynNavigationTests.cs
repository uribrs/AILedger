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
    public void DisabledOrMissingExecutableDoesNotConfigureServer(string executable)
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
                Assert.Contains("CLI search", invocation.StandardInput, StringComparison.Ordinal);
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

        var result = await new CodexAgentAdapter(runner).RunAsync(request, CancellationToken.None);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.NotNull(home);
        Assert.False(Directory.Exists(home));
    }
}
