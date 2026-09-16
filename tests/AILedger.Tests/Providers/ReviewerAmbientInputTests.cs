using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

public sealed class ReviewerAmbientInputTests
{
    // R4 (reviewer-narrative-isolation): exercise the real adapter, including launch scope,
    // rather than inferring child configuration from the neutral manifest alone.
    [Theory]
    [InlineData("codex", "reviewer")]
    [InlineData("codex", "verifier")]
    [InlineData("codex", "legacy")]
    [InlineData("claude", "reviewer")]
    [InlineData("claude", "verifier")]
    [InlineData("claude", "legacy")]
    public async Task OnlyTypedReviewerSuppressesAmbientInstructions(string provider, string kind)
    {
        var reviewer = kind == "reviewer";
        var request = ProviderProtocolTests.Request(provider, AgentLaunchMode.New, null) with
        {
            Model = "model-test",
            Environment = new Dictionary<string, string>
            {
                ["AILEDGER_TEST_INPUT"] = "preserved",
                ["CLAUDE_CODE_DISABLE_CLAUDE_MDS"] = "0",
                ["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] = "0",
                ["CLAUDE_CODE_DISABLE_ORG_MEMORY"] = "0",
                ["CLAUDE_CODE_POST_TURN_MEMORY"] = "1"
            },
            AdditionalDirectories = ["/tmp/member-a", "/tmp/member-b"],
            Assurance = kind == "legacy" ? null : new AssuranceBinding(1,
                [new WorkItemId("W1")], [new(new WorkItemId("W1"), new RunId("WORK1"))],
                new string('a', 64), reviewer ? new RunId("VERIFY1") : null)
        };
        var runner = new ScriptedProcessRunner();
        if (provider == "codex")
        {
            runner.Enqueue(0, ["codex-cli 1.2.3"])
                .Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"])
                .Enqueue(0, ["SESSION_ID --json"])
                .Enqueue(invocation =>
                {
                    var home = invocation.Environment["CODEX_HOME"];
                    Assert.True(File.Exists(Path.Combine(home, "auth.json")));
                    Assert.NotNull(new FileInfo(Path.Combine(home, "auth.json")).LinkTarget);
                    Assert.Contains("model-test", File.ReadAllText(Path.Combine(home, "config.toml")), StringComparison.Ordinal);
                    return new ScriptedProcessResult(0,
                        ["{\"type\":\"thread.started\",\"thread_id\":\"fresh-review\"}", "{\"type\":\"turn.completed\"}"], []);
                });
        }
        else
        {
            runner.Enqueue(0, ["2.0.0 (Claude Code)"])
                .Enqueue(0, ["--print --output-format --session-id --resume --permission-prompts --settings --strict-mcp-config --disable-slash-commands"])
                .Enqueue(invocation => new ScriptedProcessResult(0,
                    [$"{{\"type\":\"result\",\"session_id\":\"{ProviderProtocolTests.ValueAfter(invocation.Arguments, "--session-id")}\",\"result\":\"done\"}}"], []));
        }
        IAgentAdapter adapter = provider == "codex" ? new CodexAgentAdapter(runner) : new ClaudeAgentAdapter(runner);
        var result = await adapter.RunAsync(request, CancellationToken.None);
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        var run = runner.Invocations.Last();
        Assert.Equal(request.WorkingDirectory, run.WorkingDirectory);
        foreach (var directory in request.AdditionalDirectories) Assert.Contains(directory, run.Arguments);
        foreach (var variable in request.Environment)
        {
            var expected = provider == "claude" && reviewer && variable.Key != "AILEDGER_TEST_INPUT"
                ? variable.Key == "CLAUDE_CODE_POST_TURN_MEMORY" ? "0" : "1"
                : variable.Value;
            Assert.Equal(expected, run.Environment[variable.Key]);
        }
        Assert.DoesNotContain("--bare", run.Arguments);
        Assert.DoesNotContain("--resume", run.Arguments);
        string brief;
        if (provider == "codex")
        {
            Assert.Contains("--strict-config", run.Arguments);
            Assert.Equal("workspace-write", ProviderProtocolTests.ValueAfter(run.Arguments, "--sandbox"));
            Assert.Equal(request.WorkingDirectory, ProviderProtocolTests.ValueAfter(run.Arguments, "--cd"));
            var controls = run.Arguments.Select((value, index) => (value, index))
                .Where(pair => pair.value is "-c" or "--config")
                .Select(pair => run.Arguments[pair.index + 1]).ToArray();
            Assert.Equal(reviewer ? new[] { "project_doc_max_bytes=0" } : [], controls);
            Assert.False(Directory.Exists(run.Environment["CODEX_HOME"]));
            brief = run.StandardInput;
        }
        else
        {
            Assert.Equal("none", ProviderProtocolTests.ValueAfter(run.Arguments, "--permission-prompts"));
            Assert.Equal("acceptEdits", ProviderProtocolTests.ValueAfter(run.Arguments, "--permission-mode"));
            var settingsText = ProviderProtocolTests.ValueAfter(run.Arguments, "--settings");
            using var settings = JsonDocument.Parse(settingsText);
            var sandbox = settings.RootElement.GetProperty("sandbox");
            Assert.Equal(5, sandbox.EnumerateObject().Count());
            Assert.True(sandbox.GetProperty("enabled").GetBoolean());
            Assert.True(sandbox.GetProperty("failIfUnavailable").GetBoolean());
            Assert.True(sandbox.GetProperty("autoAllowBashIfSandboxed").GetBoolean());
            Assert.False(sandbox.GetProperty("allowUnsandboxedCommands").GetBoolean());
            Assert.Empty(sandbox.GetProperty("excludedCommands").EnumerateArray());
            Assert.Equal(reviewer ? 2 : 1, settings.RootElement.EnumerateObject().Count());
            Assert.Equal(reviewer, settings.RootElement.TryGetProperty("env", out var settingsEnvironment));
            if (reviewer)
            {
                Assert.Equal(4, settingsEnvironment.EnumerateObject().Count());
                foreach (var variable in request.Environment.Where(pair => pair.Key != "AILEDGER_TEST_INPUT"))
                {
                    var expected = variable.Key == "CLAUDE_CODE_POST_TURN_MEMORY" ? "0" : "1";
                    Assert.Equal(expected, settingsEnvironment.GetProperty(variable.Key).GetString());
                }
            }
            Assert.Equal("model-test", ProviderProtocolTests.ValueAfter(run.Arguments, "--model"));
            Assert.Contains("--strict-mcp-config", run.Arguments);
            Assert.Contains("--disable-slash-commands", run.Arguments);
            Assert.False(run.Environment.ContainsKey("HOME"));
            Assert.False(run.Environment.ContainsKey("CLAUDE_CONFIG_DIR"));
            Assert.True(Guid.TryParse(ProviderProtocolTests.ValueAfter(run.Arguments, "--session-id"), out _));
            Assert.Equal(request.StandardInput, run.StandardInput);
            brief = ProviderProtocolTests.ValueAfter(run.Arguments, "-p");
        }
        if (reviewer)
        {
            foreach (var name in new[] { "AGENTS.md", "AGENTS.override.md", "CLAUDE.md", "CLAUDE.local.md" })
                Assert.Contains(name, brief, StringComparison.Ordinal);
            Assert.Contains("do not open", brief, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("neutral manifest", brief, StringComparison.OrdinalIgnoreCase);
        }
    }
}
