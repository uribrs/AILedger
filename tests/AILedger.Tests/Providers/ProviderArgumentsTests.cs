using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

public sealed class ProviderArgumentsTests
{
    [Fact]
    public async Task CodexNewRunUsesGovernedNonInteractiveArgumentsAndStdin()
    {
        var runner = new ScriptedProcessRunner()
            .Enqueue(0, ["codex-cli 1.2.3"])
            .Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"])
            .Enqueue(0, ["SESSION_ID --json"])
            .Enqueue(0,
                ["{\"type\":\"thread.started\",\"thread_id\":\"new-session\"}",
                 "{\"type\":\"turn.completed\",\"output_text\":\"done\"}"]);
        var request = ProviderProtocolTests.Request("codex", AgentLaunchMode.New, null) with
        {
            Model = "gpt-test",
            OutputSchema = "schema.json",
            AdditionalDirectories = ["/tmp/a", "/tmp/b"]
        };

        var result = await new CodexAgentAdapter(runner).RunAsync(request, CancellationToken.None);

        var run = runner.Invocations[3];
        Assert.Equal(["exec", "--help"], runner.Invocations[1].Arguments);
        Assert.Equal(["exec", "resume", "--help"], runner.Invocations[2].Arguments);
        Assert.Equal(request.StandardInput, run.StandardInput);
        Assert.Equal(
            ["exec", "--strict-config", "--sandbox", "workspace-write", "--cd", request.WorkingDirectory,
             "--model", "gpt-test", "--output-schema", "schema.json", "--add-dir", "/tmp/a", "--add-dir", "/tmp/b",
             "--json", "--color", "never", "-"],
            run.Arguments);
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal("new-session", result.ProviderSessionId);
    }

    [Fact]
    public async Task CodexResumePinsExactSession()
    {
        var runner = new ScriptedProcessRunner()
            .Enqueue(0, ["codex-cli 1.2.3"])
            .Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"])
            .Enqueue(0, ["SESSION_ID --json"])
            .Enqueue(0,
                ["{\"type\":\"thread.started\",\"thread_id\":\"session-1\"}", "{\"type\":\"turn.completed\"}"]);

        var result = await new CodexAgentAdapter(runner).RunAsync(
            ProviderProtocolTests.Request("codex", AgentLaunchMode.Resume, "session-1"), CancellationToken.None);

        Assert.Equal(["exec", "--strict-config", "--sandbox", "workspace-write", "--cd", Path.GetTempPath(),
            "resume", "--json", "session-1", "-"], runner.Invocations[3].Arguments);
        Assert.True(result.IsResume);
    }

    [Fact]
    public async Task ClaudeNewRunPreassignsSessionAndAppliesSandboxSettings()
    {
        var runner = new ScriptedProcessRunner()
            .Enqueue(0, ["2.0.0 (Claude Code)"])
            .Enqueue(0, ["--print --output-format --session-id --resume --permission-prompts --settings --strict-mcp-config --disable-slash-commands"])
            .Enqueue(invocation =>
            {
                var session = ProviderProtocolTests.ValueAfter(invocation.Arguments, "--session-id");
                return new ScriptedProcessResult(0,
                    [$"{{\"type\":\"result\",\"session_id\":\"{session}\",\"result\":\"done\"}}"], []);
            });
        var request = ProviderProtocolTests.Request("claude", AgentLaunchMode.New, null) with
        {
            AdditionalDirectories = ["/tmp/a", "/tmp/b"]
        };

        var result = await new ClaudeAgentAdapter(runner).RunAsync(request, CancellationToken.None);

        var arguments = runner.Invocations[2].Arguments;
        Assert.Contains("--permission-prompts", arguments);
        Assert.Equal("none", ProviderProtocolTests.ValueAfter(arguments, "--permission-prompts"));
        Assert.Contains("--strict-mcp-config", arguments);
        Assert.Contains("--disable-slash-commands", arguments);
        Assert.Contains("failIfUnavailable", ProviderProtocolTests.ValueAfter(arguments, "--settings"), StringComparison.Ordinal);
        Assert.Equal("/tmp/a", ProviderProtocolTests.ValueAfter(arguments, "--add-dir"));
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal("done", result.FinalOutput);
    }

    [Fact]
    public async Task MissingCliCapabilityFailsBeforeLaunch()
    {
        var runner = new ScriptedProcessRunner()
            .Enqueue(0, ["codex-cli 1.2.3"])
            .Enqueue(0, ["--version only"]);

        var exception = await Assert.ThrowsAsync<AgentAdapterException>(() =>
            new CodexAgentAdapter(runner).RunAsync(
                ProviderProtocolTests.Request("codex", AgentLaunchMode.New, null), CancellationToken.None));

        Assert.Contains("capability probe", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, runner.Invocations.Count);
    }

    [Fact]
    public async Task ResumeWithoutSessionIsRejectedBeforeAnyProcessStarts()
    {
        var runner = new ScriptedProcessRunner();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ClaudeAgentAdapter(runner).RunAsync(
                ProviderProtocolTests.Request("claude", AgentLaunchMode.Resume, null), CancellationToken.None));

        Assert.Empty(runner.Invocations);
    }
}
