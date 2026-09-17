using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Providers.Process;

namespace AILedger.Tests.Providers;

public sealed class ProtocolRecordRetentionTests
{
    private static readonly string LargeText = new('x', ProviderOutputLimits.MaximumCharactersPerLine + 100);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedPayloadPreservesSuccessfulProtocol(bool claude)
    {
        var lines = claude
            ? new[] { JsonSerializer.Serialize(new { type = "user", session_id = "s",
                message = new { content = new[] { new { type = "tool_result", content = LargeText } } } }),
                "{\"type\":\"result\",\"session_id\":\"s\",\"result\":\"done\"}" }
            : new[] { "{\"type\":\"thread.started\",\"thread_id\":\"s\"}",
                JsonSerializer.Serialize(new { type = "item.completed", item = new { aggregated_output = LargeText } }),
                "{\"type\":\"turn.completed\",\"output_text\":\"done\"}" };

        var result = await RunAsync(lines, claude);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.TruncatedLines);
        Assert.Equal("done", result.FinalOutput);
        Assert.Equal("s", result.ProviderSessionId);
        Assert.Equal(lines, result.Events.Select(e => e.RawJson));
    }

    [Theory]
    [InlineData("malformed", AgentRunStatus.ProtocolError)]
    [InlineData("missing-terminal", AgentRunStatus.ProtocolError)]
    [InlineData("conflicting-session", AgentRunStatus.ProtocolError)]
    [InlineData("provider-error", AgentRunStatus.Failed)]
    public async Task OversizedRecordDoesNotHideProtocolFailures(string failure, AgentRunStatus expected)
    {
        var lines = new List<string> { "{\"type\":\"thread.started\",\"thread_id\":\"s\"}",
            JsonSerializer.Serialize(new { type = "item.completed", item = new { aggregated_output = LargeText } }) };
        if (failure == "malformed") lines.Add("{broken");
        if (failure == "conflicting-session") lines.Add("{\"type\":\"thread.started\",\"thread_id\":\"other\"}");
        if (failure != "missing-terminal") lines.Add(failure == "provider-error"
            ? "{\"type\":\"turn.failed\"}" : "{\"type\":\"turn.completed\"}");

        var result = await RunAsync(lines);

        Assert.Equal(expected, result.Status);
        Assert.Equal(0, result.TruncatedLines);
    }

    [Fact]
    public async Task ErrorFlagAfterOversizedClaudeResultSurvives()
    {
        var result = await RunAsync([JsonSerializer.Serialize(new
            { type = "result", result = LargeText, session_id = "s", is_error = true })], claude: true);

        Assert.Equal(AgentRunStatus.Failed, result.Status);
        Assert.True(Assert.Single(result.Events).IsError);
        Assert.Equal(0, result.TruncatedLines);
        Assert.Equal(LargeText, result.FinalOutput);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedFinalAnswerKeepsItsEnding(bool claude)
    {
        var answer = LargeText + " Whatever you do, never delete the backup.";
        var lines = claude
            ? new[] { JsonSerializer.Serialize(new { type = "result", session_id = "s", result = answer }) }
            : new[] { "{\"type\":\"thread.started\",\"thread_id\":\"s\"}",
                JsonSerializer.Serialize(new { type = "turn.completed", output_text = answer }) };

        var result = await RunAsync(lines, claude);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal(answer, result.FinalOutput);
        Assert.Equal(0, result.TruncatedLines);
    }

    [Fact]
    public async Task MalformedTailOfOversizedRecordIsStillRejected()
    {
        var line = JsonSerializer.Serialize(new { type = "item.completed", text = LargeText });
        var result = await RunAsync([line[..^1], "{\"type\":\"turn.completed\"}"]);

        Assert.Equal(AgentRunStatus.ProtocolError, result.Status);
        Assert.Contains("Malformed provider JSONL", result.Failure, StringComparison.Ordinal);
        Assert.Equal(0, result.TruncatedLines);
    }

    [Fact]
    public async Task StreamCeilingStillRejectsAnUnboundedRecord()
    {
        var line = JsonSerializer.Serialize(new { type = "item.completed",
            text = new string('x', ProviderOutputLimits.MaximumCharactersPerStream) });
        var result = await RunAsync([line]);

        Assert.Equal(AgentRunStatus.ProtocolError, result.Status);
        Assert.Contains("per-stream limit", result.Failure, StringComparison.Ordinal);
        Assert.Empty(result.Events);
    }

    private static async Task<AgentRunResult> RunAsync(IEnumerable<string> lines, bool claude = false)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(path, lines);
            var request = ProviderProtocolTests.Request("probe", AgentLaunchMode.New, null);
            return await new PayloadAdapter(path, claude).RunAsync(request, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Only the version probe is stubbed. Protocol bytes cross the actual process pipe and reader.
    private sealed class PayloadRunner : IProcessRunner
    {
        public async Task<ProcessExit> RunAsync(ProcessInvocation invocation,
            Func<string, CancellationToken, ValueTask> stdout, Func<string, CancellationToken, ValueTask> stderr,
            TruncatedLineTally? tally, CancellationToken cancellationToken)
        {
            if (invocation.Arguments is ["--version"])
            {
                await stdout("test-provider", cancellationToken);
                return new ProcessExit(0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0);
            }
            return await new SystemProcessRunner().RunAsync(invocation with
                { ExecutablePath = "/bin/cat", StandardInput = string.Empty },
                stdout, stderr, tally, cancellationToken);
        }
    }

    private sealed class PayloadAdapter(string path, bool claude) : AgentAdapterBase(new PayloadRunner())
    {
        public override string Provider => "probe";
        protected override IReadOnlyList<CapabilityProbe> CapabilityProbes => [];
        protected override IReadOnlyList<string> BuildArguments(AgentLaunchRequest request, ref string? sessionId) => [path];
        protected override ProviderEvent ParseEvent(long sequence, string json) => claude
            ? ProviderProtocol.ParseClaude(sequence, json) : ProviderProtocol.ParseCodex(sequence, json);
        protected override string? ReadFinalOutput(ProviderEvent providerEvent) => providerEvent.IsTerminal
            ? ProviderProtocol.ReadString(providerEvent.RawJson, claude ? "result" : "output_text") : null;
    }
}
