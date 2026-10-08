using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

public sealed class ProviderIncidentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaudeHeadlessHandoffPermissionMatchesHostGrant(bool granted)
    {
        var runner = new ScriptedProcessRunner()
            .Enqueue(0, ["fixture"])
            .Enqueue(0, ["--print --output-format --session-id --resume --permission-prompts --settings --strict-mcp-config --disable-slash-commands"])
            .Enqueue(invocation =>
            {
                var allowed = ProviderProtocolTests.ValueAfter(invocation.Arguments, "--allowedTools").Split(',');
                Assert.Equal(granted, allowed.Contains("mcp__ailedger__cognitive_handoff"));
                Assert.DoesNotContain("mcp__ailedger__*", allowed);
                var session = ProviderProtocolTests.ValueAfter(invocation.Arguments, "--session-id");
                return new(0, [JsonSerializer.Serialize(new { type = "result", session_id = session, result = "done" })], []);
            });
        var request = ProviderProtocolTests.Request("claude", AgentLaunchMode.New, null) with
        { FindingsEndpoint = new("/trusted/dotnet", ["host.dll"], AllowCognitiveHandoffs: granted) };
        Assert.Equal(AgentRunStatus.Completed, (await new ClaudeAgentAdapter(runner).RunAsync(request, default)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaudeShellPermissionRequiresOuterOsConfinement(bool confined)
    {
        var runner = new ScriptedProcessRunner()
            .Enqueue(0, ["fixture"])
            .Enqueue(0, ["--print --output-format --session-id --resume --permission-prompts --settings --strict-mcp-config --disable-slash-commands"])
            .Enqueue(invocation =>
            {
                using var settings = JsonDocument.Parse(ProviderProtocolTests.ValueAfter(invocation.Arguments, "--settings"));
                Assert.Equal(!confined, settings.RootElement.GetProperty("sandbox").GetProperty("enabled").GetBoolean());
                Assert.Equal(confined, invocation.Isolation is not null);
                if (confined)
                {
                    Assert.Equal("Bash", ProviderProtocolTests.ValueAfter(invocation.Arguments, "--allowedTools"));
                    Assert.DoesNotContain("--dangerously-skip-permissions", invocation.Arguments);
                }
                else Assert.DoesNotContain("--allowedTools", invocation.Arguments);
                var session = ProviderProtocolTests.ValueAfter(invocation.Arguments, "--session-id");
                return new(0, [JsonSerializer.Serialize(new { type = "result", session_id = session, result = "done" })], []);
            });
        var request = ProviderProtocolTests.Request("claude", AgentLaunchMode.New, null) with
        { Isolation = confined ? new([Path.GetTempPath()], [], []) : null };
        Assert.Equal(AgentRunStatus.Completed, (await new ClaudeAgentAdapter(runner).RunAsync(request, default)).Status);
    }

    [Fact]
    public async Task CodexAcceptsExactComposedLimitAndRejectsOneMoreCharacter()
    {
        static ScriptedProcessRunner Runner() => new ScriptedProcessRunner()
            .Enqueue(0, ["codex-cli fixture"])
            .Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"])
            .Enqueue(0, ["SESSION_ID --json"])
            .Enqueue(0, ["{\"type\":\"thread.started\",\"thread_id\":\"session\"}", "{\"type\":\"turn.completed\"}"]);
        var request = ProviderProtocolTests.Request("codex", AgentLaunchMode.New, null) with { StandardInput = "" };
        var first = Runner();
        await new CodexAgentAdapter(first).RunAsync(request, default);
        var overhead = first.Invocations.Last().StandardInput.Length;
        var boundary = request with { StandardInput = new string('x', 1048576 - overhead) };
        var accepted = Runner();
        Assert.Equal(AgentRunStatus.Completed, (await new CodexAgentAdapter(accepted).RunAsync(boundary, default)).Status);
        Assert.Equal(1048576, accepted.Invocations.Last().StandardInput.Length);
        var refused = new ScriptedProcessRunner();
        await Assert.ThrowsAsync<AgentAdapterException>(() => new CodexAgentAdapter(refused).RunAsync(boundary with
            { StandardInput = boundary.StandardInput + "x" }, default));
        Assert.Empty(refused.Invocations);
    }

    [Fact]
    public async Task CodexRejectsComposedInputBeforeStartingAnyProcess()
    {
        var runner = new ScriptedProcessRunner();
        var request = ProviderProtocolTests.Request("codex", AgentLaunchMode.New, null) with
        { StandardInput = new string('x', 1048576 - 1) };
        var error = await Assert.ThrowsAsync<AgentAdapterException>(() => new CodexAgentAdapter(runner).RunAsync(request, default));
        Assert.Contains("1048576", error.Message);
        Assert.Contains("composed", error.Message);
        Assert.Empty(runner.Invocations);
    }
}
