using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

public sealed class ProviderFindingsConfigurationTests
{
    [Theory]
    [InlineData("claude", false, false)]
    [InlineData("claude", true, false)]
    [InlineData("codex", false, false)]
    [InlineData("codex", true, false)]
    [InlineData("claude", false, true)]
    [InlineData("codex", false, true)]
    public async Task InlineEndpointAndExactToolGrantDoNotDependOnNavigation(string provider, bool navigation, bool resume)
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));
        var request = ProviderProtocolTests.Request(provider, resume ? AgentLaunchMode.Resume : AgentLaunchMode.New,
            resume ? "session-1" : null) with
        {
            WorkingDirectory = directory.Path,
            NavigationHostAssembly = navigation ? typeof(CliApplication).Assembly.Location : null,
            FindingsEndpoint = new("/trusted/dotnet", ["/trusted/host.dll", "findings", "relay", "12345", new string('A', 64)])
        };
        var runner = new ScriptedProcessRunner().Enqueue(0, ["test-version"]);
        if (provider == "codex")
            runner.Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"])
                .Enqueue(0, ["SESSION_ID --json"]);
        else
            runner.Enqueue(0, ["--print --output-format --session-id --resume --permission-prompts --settings --strict-mcp-config --disable-slash-commands"]);
        runner.Enqueue(invocation =>
        {
            string briefing;
            if (provider == "codex")
            {
                var config = ProviderProtocolTests.ValueAfter(invocation.Arguments, "--config");
                Assert.StartsWith("mcp_servers.ailedger=", config);
                Assert.Contains("enabled_tools=[\"record_findings\",\"record_alternatives\",\"submit_artifact\",\"record_claim_dispositions\",\"inspect_task\",\"retrieve_context\",\"check_readiness\"]", config);
                Assert.Contains("tools={record_findings={approval_mode=\"approve\"},record_alternatives={approval_mode=\"approve\"},submit_artifact={approval_mode=\"approve\"},record_claim_dispositions={approval_mode=\"approve\"},inspect_task={approval_mode=\"approve\"},retrieve_context={approval_mode=\"approve\"},check_readiness={approval_mode=\"approve\"}}", config);
                Assert.Contains("required=true", config);
                Assert.DoesNotContain("mcp_servers.ailedger", File.ReadAllText(Path.Combine(invocation.Environment["CODEX_HOME"], "config.toml")));
                briefing = invocation.StandardInput;
            }
            else
            {
                using var config = JsonDocument.Parse(ProviderProtocolTests.ValueAfter(invocation.Arguments, "--mcp-config"));
                var servers = config.RootElement.GetProperty("mcpServers");
                Assert.Equal(navigation, servers.TryGetProperty("roslyn", out _));
                var endpoint = servers.GetProperty("ailedger");
                Assert.Equal(request.FindingsEndpoint.Command, endpoint.GetProperty("command").GetString());
                Assert.Equal(request.FindingsEndpoint.Arguments, endpoint.GetProperty("args").EnumerateArray().Select(v => v.GetString()));
                var grants = ProviderProtocolTests.ValueAfter(invocation.Arguments, "--allowedTools").Split(',');
                Assert.Contains("mcp__ailedger__record_findings", grants);
                Assert.Contains("mcp__ailedger__record_alternatives", grants);
                Assert.Contains("mcp__ailedger__record_claim_dispositions", grants);
                Assert.Contains("mcp__ailedger__submit_artifact", grants);
                Assert.Contains("mcp__ailedger__inspect_task", grants);
                Assert.Contains("mcp__ailedger__retrieve_context", grants);
                Assert.Contains("mcp__ailedger__check_readiness", grants);
                Assert.DoesNotContain("mcp__ailedger__record_artifact", grants);
                Assert.DoesNotContain("mcp__ailedger__*", grants);
                briefing = ProviderProtocolTests.ValueAfter(invocation.Arguments, "-p");
            }
            Assert.Contains("retry exactly that body/key", briefing);
            Assert.DoesNotContain("claim add --task", briefing);
            Assert.DoesNotContain("evidence add --task", briefing);
            Assert.DoesNotContain("alternative record --task", briefing);
            Assert.Contains("record_alternatives", briefing);
            Assert.Contains("record_claim_dispositions", briefing);
            Assert.Contains("judgment is correct", briefing);
            Assert.Contains("submit_artifact", briefing);
            Assert.Contains("inspect_task", briefing);
            Assert.Contains("check_readiness", briefing);
            Assert.Contains("never a grant", briefing);
            Assert.Contains("old recorded assignments do not change", briefing);
            Assert.Equal(request.AdditionalDirectories.Count, invocation.Arguments.Count(a => a == "--add-dir"));
            var session = resume ? "session-1" : provider == "claude"
                ? ProviderProtocolTests.ValueAfter(invocation.Arguments, "--session-id") : "actual-session";
            return new ScriptedProcessResult(0, provider == "claude"
                ? [JsonSerializer.Serialize(new { type = "result", session_id = session, result = "done" })]
                : [JsonSerializer.Serialize(new { type = "thread.started", thread_id = session }), "{\"type\":\"turn.completed\"}"], []);
        });
        IAgentAdapter adapter = provider == "codex"
            ? new CodexAgentAdapter(runner, (_, _, _, _, _) => Task.CompletedTask) : new ClaudeAgentAdapter(runner);
        Assert.Equal(AgentRunStatus.Completed, (await adapter.RunAsync(request, default)).Status);
    }
}
