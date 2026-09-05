using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Tests.Support;
using System.Text.Json;

namespace AILedger.Tests.Providers;

public sealed class ProviderProtocolTests
{
    [Fact]
    public async Task R5_SuccessRequiresExitZeroSuccessfulTerminalAndMatchingSession()
    {
        var success = await RunCodexAsync(0,
            "{\"type\":\"thread.started\",\"thread_id\":\"session-1\"}",
            "{\"type\":\"turn.completed\",\"output_text\":\"done\"}");
        var nonZero = await RunCodexAsync(7,
            "{\"type\":\"thread.started\",\"thread_id\":\"session-1\"}",
            "{\"type\":\"turn.completed\"}");
        var failedTerminal = await RunCodexAsync(0,
            "{\"type\":\"thread.started\",\"thread_id\":\"session-1\"}",
            "{\"type\":\"turn.failed\"}");
        var missingTerminal = await RunCodexAsync(0,
            "{\"type\":\"thread.started\",\"thread_id\":\"session-1\"}");
        var mismatchedSession = await RunCodexAsync(0,
            "{\"type\":\"thread.started\",\"thread_id\":\"wrong-session\"}",
            "{\"type\":\"turn.completed\"}");

        Assert.Equal(AgentRunStatus.Completed, success.Status);
        Assert.Null(success.Failure);
        Assert.Equal("done", success.FinalOutput);
        Assert.Equal(AgentRunStatus.Failed, nonZero.Status);
        Assert.Equal(AgentRunStatus.Failed, failedTerminal.Status);
        Assert.Equal(AgentRunStatus.ProtocolError, missingTerminal.Status);
        Assert.Equal(AgentRunStatus.ProtocolError, mismatchedSession.Status);
        Assert.Contains("session identities", mismatchedSession.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedJsonCannotBeMaskedByLaterSuccessfulTerminal()
    {
        var result = await RunCodexAsync(0,
            "{not-json}",
            "{\"type\":\"thread.started\",\"thread_id\":\"session-1\"}",
            "{\"type\":\"turn.completed\"}");

        Assert.Equal(AgentRunStatus.ProtocolError, result.Status);
        Assert.Contains("Malformed provider JSONL", result.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClaudeErrorResultIsFailedAndRetainsSessionAndOutput()
    {
        var runner = ClaudeRunner(invocation =>
        {
            var session = ValueAfter(invocation.Arguments, "--session-id");
            return new ScriptedProcessResult(0,
                [$"{{\"type\":\"system\",\"session_id\":\"{session}\"}}",
                 $"{{\"type\":\"result\",\"session_id\":\"{session}\",\"is_error\":true,\"result\":\"failed\"}}"],
                []);
        });

        var result = await new ClaudeAgentAdapter(runner).RunAsync(Request("claude", AgentLaunchMode.New, null), CancellationToken.None);

        Assert.Equal(AgentRunStatus.Failed, result.Status);
        Assert.NotNull(result.ProviderSessionId);
        Assert.Equal("failed", result.FinalOutput);
    }

    [Fact]
    public async Task ClaudeRejectsAnyConflictingSessionEventEvenWhenTerminalMatches()
    {
        var runner = ClaudeRunner(invocation =>
        {
            var session = ValueAfter(invocation.Arguments, "--session-id");
            return new ScriptedProcessResult(0,
                ["{\"type\":\"system\",\"session_id\":\"wrong-session\"}",
                 $"{{\"type\":\"result\",\"session_id\":\"{session}\",\"result\":\"done\"}}"],
                []);
        });

        var result = await new ClaudeAgentAdapter(runner).RunAsync(
            Request("claude", AgentLaunchMode.New, null), CancellationToken.None);

        Assert.Equal(AgentRunStatus.ProtocolError, result.Status);
        Assert.Contains("conflicting session identities", result.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardErrorSecretsAreRedacted()
    {
        var runner = CodexRunner(0,
            ["{\"type\":\"thread.started\",\"thread_id\":\"session-1\"}", "{\"type\":\"turn.completed\"}"],
            ["token secret-value rejected"]);
        var request = Request("codex", AgentLaunchMode.Resume, "session-1") with
        {
            Environment = new Dictionary<string, string> { ["API_TOKEN"] = "secret-value" }
        };

        var result = await new CodexAgentAdapter(runner).RunAsync(request, CancellationToken.None);

        Assert.DoesNotContain("secret-value", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StdoutAndFinalOutputSecretsAreRedactedWithoutBreakingRetainedJson()
    {
        const string secret = "secret-\"value";
        var outputLine = JsonSerializer.Serialize(new
        {
            type = "turn.completed",
            output_text = $"provider echoed {secret}"
        });
        var runner = CodexRunner(0,
            ["{\"type\":\"thread.started\",\"thread_id\":\"session-1\"}", outputLine],
            [$"stderr echoed {secret}"]);
        var request = Request("codex", AgentLaunchMode.Resume, "session-1") with
        {
            Environment = new Dictionary<string, string> { ["API_TOKEN"] = secret }
        };

        var result = await new CodexAgentAdapter(runner).RunAsync(request, CancellationToken.None);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.DoesNotContain(secret, result.FinalOutput, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.FinalOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.StandardError, StringComparison.Ordinal);
        Assert.All(result.Events, providerEvent =>
        {
            Assert.DoesNotContain(secret, providerEvent.RawJson, StringComparison.Ordinal);
            JsonDocument.Parse(providerEvent.RawJson).Dispose();
        });
        Assert.Contains("[REDACTED]", result.Events[^1].RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorTerminalCannotBeMaskedByLaterSuccessfulTerminal()
    {
        var result = await RunCodexAsync(0,
            "{\"type\":\"thread.started\",\"thread_id\":\"session-1\"}",
            "{\"type\":\"error\",\"message\":\"failed\"}",
            "{\"type\":\"turn.completed\"}");

        Assert.Equal(AgentRunStatus.Failed, result.Status);
        Assert.Contains("failed event", result.Failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MultipleSuccessfulTerminalsAreRejected()
    {
        var result = await RunCodexAsync(0,
            "{\"type\":\"thread.started\",\"thread_id\":\"session-1\"}",
            "{\"type\":\"turn.completed\"}",
            "{\"type\":\"turn.completed\"}");

        Assert.Equal(AgentRunStatus.ProtocolError, result.Status);
        Assert.Contains("exactly one terminal", result.Failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EventAfterSuccessfulTerminalIsRejected()
    {
        var result = await RunCodexAsync(0,
            "{\"type\":\"thread.started\",\"thread_id\":\"session-1\"}",
            "{\"type\":\"turn.completed\"}",
            "{\"type\":\"item.completed\",\"item\":{\"text\":\"late\"}}");

        Assert.Equal(AgentRunStatus.ProtocolError, result.Status);
        Assert.Contains("final stream event", result.Failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClaudeEarlierErrorResultCannotBeMaskedBySuccessfulResult()
    {
        var runner = ClaudeRunner(invocation =>
        {
            var session = ValueAfter(invocation.Arguments, "--session-id");
            return new ScriptedProcessResult(0,
                [$"{{\"type\":\"result\",\"session_id\":\"{session}\",\"is_error\":true,\"result\":\"failed\"}}",
                 $"{{\"type\":\"result\",\"session_id\":\"{session}\",\"result\":\"done\"}}"],
                []);
        });

        var result = await new ClaudeAgentAdapter(runner).RunAsync(
            Request("claude", AgentLaunchMode.New, null), CancellationToken.None);

        Assert.Equal(AgentRunStatus.Failed, result.Status);
        Assert.Contains("failed event", result.Failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OversizedProviderOutputFailsWithBoundedProtocolResult()
    {
        var oversized = new string('x', 1024 * 1024 + 1);
        var runner = CodexRunner(0, [oversized], []);

        var result = await new CodexAgentAdapter(runner).RunAsync(
            Request("codex", AgentLaunchMode.Resume, "session-1"), CancellationToken.None);

        Assert.Equal(AgentRunStatus.ProtocolError, result.Status);
        Assert.Contains("output exceeded", result.Failure, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task VersionProbeUsesStdoutDeterministicallyWhenStderrIsConcurrent()
    {
        var version = await new CodexAgentAdapter(new ConcurrentVersionRunner())
            .ProbeVersionAsync("/usr/bin/true", CancellationToken.None);

        Assert.Equal("stdout-version", version);
    }

    private static async Task<AgentRunResult> RunCodexAsync(int exitCode, params string[] output)
    {
        var runner = CodexRunner(exitCode, output, []);
        return await new CodexAgentAdapter(runner).RunAsync(
            Request("codex", AgentLaunchMode.Resume, "session-1"), CancellationToken.None);
    }

    private static ScriptedProcessRunner CodexRunner(
        int exitCode,
        IReadOnlyList<string> runOutput,
        IReadOnlyList<string> runError) =>
        new ScriptedProcessRunner()
            .Enqueue(0, ["codex-cli 1.2.3"])
            .Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"])
            .Enqueue(0, ["SESSION_ID --json"])
            .Enqueue(exitCode, runOutput, runError);

    private static ScriptedProcessRunner ClaudeRunner(Func<ProcessInvocation, ScriptedProcessResult> run) =>
        new ScriptedProcessRunner()
            .Enqueue(0, ["2.0.0 (Claude Code)"])
            .Enqueue(0, ["--print --output-format --session-id --resume --permission-prompts --settings --strict-mcp-config --disable-slash-commands"])
            .Enqueue(run);

    internal static AgentLaunchRequest Request(string provider, AgentLaunchMode mode, string? sessionId) => new(
        new RunId("R1"),
        new TaskId("T1"),
        new ActorId("lead"),
        new WorkItemId("W1"),
        mode,
        provider,
        "/usr/bin/agent",
        Path.GetTempPath(),
        "governed context",
        sessionId,
        PermissionProfile.WorkspaceGoverned,
        null,
        null,
        [],
        new Dictionary<string, string>(),
        TimeSpan.FromSeconds(30));

    internal static string ValueAfter(IReadOnlyList<string> arguments, string flag) =>
        arguments[arguments.IndexOf(flag) + 1];

    private sealed class ConcurrentVersionRunner : IProcessRunner
    {
        public async Task<ProcessExit> RunAsync(
            ProcessInvocation invocation,
            Func<string, CancellationToken, ValueTask> onStandardOutputLine,
            Func<string, CancellationToken, ValueTask> onStandardErrorLine,
            CancellationToken cancellationToken)
        {
            var stderr = onStandardErrorLine("warning", cancellationToken).AsTask();
            await Task.Yield();
            var stdout = onStandardOutputLine("stdout-version", cancellationToken).AsTask();
            await Task.WhenAll(stdout, stderr);
            return new ProcessExit(0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        }
    }
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value))
            {
                return index;
            }
        }

        return -1;
    }
}
