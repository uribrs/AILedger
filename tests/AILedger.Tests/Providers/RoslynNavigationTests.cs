using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
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
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public void FindsCSharpSolutionFromNestedWorkingDirectory(string extension)
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, ".git"));
        var nested = Directory.CreateDirectory(Path.Combine(root.Path, "src", "App")).FullName;
        var solution = Path.Combine(root.Path, "App" + extension);
        File.WriteAllText(solution, "App.csproj");

        Assert.Equal(solution, RoslynNavigation.FindSolution(nested));
    }

    [Fact]
    public void DoesNotBorrowSolutionFromOutsideCheckoutOrGuessAmongSolutions()
    {
        using var root = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(root.Path, "Outside.sln"), "Outside.csproj");
        var work = Directory.CreateDirectory(Path.Combine(root.Path, "work")).FullName;
        File.WriteAllText(Path.Combine(work, ".git"), "gitdir: elsewhere");
        Assert.Null(RoslynNavigation.FindSolution(work));

        File.WriteAllText(Path.Combine(work, "One.sln"), "One.csproj");
        File.WriteAllText(Path.Combine(work, "Two.slnx"), "Two.csproj");
        Assert.Null(RoslynNavigation.FindSolution(work));
    }

    [Theory]
    [InlineData("python.py")]
    [InlineData("App.vbproj")]
    public void NonCSharpSolutionUsesCli(string content)
    {
        using var root = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(root.Path, "App.sln"), content);
        Assert.Null(RoslynNavigation.FindSolution(root.Path));
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
        Assert.Empty(RoslynNavigation.Configuration(request));
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
        var solution = Path.Combine(root.Path, "App.sln");
        File.WriteAllText(solution, "App.csproj");
        string? home = null;
        var runner = new ScriptedProcessRunner()
            .Enqueue(0, ["codex-cli 1.2.3"])
            .Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"])
            .Enqueue(0, ["SESSION_ID --json"])
            .Enqueue(invocation =>
            {
                home = invocation.Environment["CODEX_HOME"];
                var config = File.ReadAllText(Path.Combine(home, "config.toml"));
                Assert.Contains($"command = {RoslynNavigation.Quote(executable)}", config, StringComparison.Ordinal);
                Assert.Contains($"args = [{RoslynNavigation.Quote(solution)}]", config, StringComparison.Ordinal);
                Assert.Contains($"enabled_tools = {JsonSerializer.Serialize(RoslynNavigation.Tools)}", config, StringComparison.Ordinal);
                Assert.Contains("required = false", config, StringComparison.Ordinal);
                Assert.Contains("default_tools_approval_mode = \"approve\"", config, StringComparison.Ordinal);
                Assert.DoesNotContain("rename_symbol", config, StringComparison.Ordinal);
                Assert.DoesNotContain("load_solution", config, StringComparison.Ordinal);
                Assert.Contains("rebuild_solution before another semantic query", invocation.StandardInput, StringComparison.Ordinal);
                Assert.Contains("CLI search", invocation.StandardInput, StringComparison.Ordinal);
                Assert.Equal(reviewer, invocation.Arguments.Contains("project_doc_max_bytes=0"));
                return new ScriptedProcessResult(0,
                    ["{\"type\":\"thread.started\",\"thread_id\":\"navigation-smoke\"}", "{\"type\":\"turn.completed\"}"], []);
            });
        var request = ProviderProtocolTests.Request("codex", AgentLaunchMode.New, null) with
        {
            WorkingDirectory = root.Path,
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
