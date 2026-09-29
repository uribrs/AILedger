using System.Text.Json;
using AILedger.Cli.Findings;
using AILedger.Tests.Findings;

namespace AILedger.Tests.Inspection;

public sealed class InspectionMcpTests
{
    internal static string Call(string tool, string body, int id = 2) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name = tool, arguments = JsonDocument.Parse(body).RootElement } });
    internal static JsonElement Body(JsonElement rpc) => rpc.GetProperty("result").GetProperty("structuredContent");

    [Theory]
    [InlineData("codex")] [InlineData("claude")]
    public async Task TrustedProviderRelaySupportsInspectionRetrievalAndReadinessWithRevocation(string provider)
    {
        using var f = new FindingsMcpFixture(); await f.OpenAsync();
        var configuration = f.Configuration with { AllowInspect = true, Provider = provider };
        await using (var host = new ProviderFindingsSession(configuration, f.Ledger.Service(), "unused", [], _ => Task.FromResult(configuration)))
        {
            using var relay = new McpProcess(host.Endpoint.Arguments);
            await ProviderFindingsSessionTests.InitializeAsync(relay);
            await relay.SendAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            var tools = (await relay.ReadAsync()).GetProperty("result").GetProperty("tools");
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "inspect_task" && t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
            await relay.SendAsync(Call("inspect_task", """{"schema_version":1}""", 3));
            var first = Body(await relay.ReadAsync()); Assert.Equal("ok", first.GetProperty("status").GetString());
            var snapshot = first.GetProperty("snapshot"); var version = snapshot.GetProperty("ledger_version").GetInt64();
            Assert.Equal("operator", snapshot.GetProperty("actor_id").GetString());
            var reference = first.GetProperty("records")[0].GetProperty("retrieve");
            await relay.SendAsync(Call("retrieve_context", reference.GetRawText(), 4));
            Assert.Equal("ok", Body(await relay.ReadAsync()).GetProperty("status").GetString());
            var ready = JsonSerializer.Serialize(new { schema_version = 1, action = "record_findings", expected_version = version,
                proposal = JsonDocument.Parse(FindingsMcpFixture.Body()).RootElement });
            await relay.SendAsync(Call("check_readiness", ready, 5));
            Assert.Equal("ready", Body(await relay.ReadAsync()).GetProperty("status").GetString());
            // Rapid sequential chunk-sized reads cannot be mistaken for eight outstanding requests
            // merely because diagnostic writes finish after their responses.
            for (var i = 0; i < 32; i++)
            {
                await relay.SendAsync(Call("retrieve_context", reference.GetRawText(), 10 + i));
                Assert.Equal("ok", Body(await relay.ReadAsync()).GetProperty("status").GetString());
            }
            configuration = configuration with { AllowInspect = false };
            await relay.SendAsync(Call("inspect_task", """{"schema_version":1}""", 6));
            Assert.Equal("authorization_denied", Body(await relay.ReadAsync()).GetProperty("diagnostic").GetProperty("code").GetString());
            Assert.Empty(await relay.FinishAsync());
        } // Drain the remote host's diagnostics before asserting on complete telemetry.
        Assert.Empty((await f.Ledger.StateAsync()).Claims);
        var attempts = (await f.AttemptsAsync()).Where(a => a.TryGetProperty("population", out _)).ToArray();
        Assert.Equal(36, attempts.Length);
        Assert.All(attempts, row =>
        {
            Assert.Equal("inspection", row.GetProperty("population").GetString());
            Assert.Equal(provider, row.GetProperty("provider").GetString());
            Assert.False(row.GetProperty("durable_request").GetBoolean());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("provider_usage").ValueKind);
        });
        Assert.Empty(Directory.GetFiles(f.Ledger.Directory, "*attempt*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("inspect_task", "{}")]
    [InlineData("inspect_task", "{\"schema_version\":1,\"actor_id\":\"operator\"}")]
    [InlineData("inspect_task", "{\"schema_version\":1,\"schema_version\":1}")]
    [InlineData("inspect_task", "{\"schema_version\":1,\"selection\":\"all_secrets\"}")]
    [InlineData("check_readiness", "{\"schema_version\":1,\"action\":\"record_findings\",\"expected_version\":2,\"actor_id\":\"operator\"}")]
    [InlineData("check_readiness", "{\"schema_version\":1,\"action\":\"transition_stage\",\"expected_version\":2,\"proposal\":{\"stage\":\"research\",\"without_prerequisites_reason\":\"waive\"}}")]
    [InlineData("check_readiness", "{\"schema_version\":1,\"action\":\"prepare_work\",\"expected_version\":2,\"proposal\":{\"id\":\"W1\",\"title\":\"Work\",\"scope\":[],\"claims\":[],\"skills_served_now\":[]}}")]
    public async Task PayloadCannotSupplyAuthorityOrWaivers(string tool, string request)
    {
        using var f = new FindingsMcpFixture(); await f.OpenAsync();
        f.Configuration = f.Configuration with { AllowInspect = true };
        var before = await InspectionTests.Files(f.Ledger.Directory);
        var result = Body(Assert.Single(await f.ExchangeAsync(Call(tool, request) + "\n")));
        Assert.Contains(result.GetProperty("status").GetString(), new[] { "error", "unknown" });
        Assert.Equal(before, await InspectionTests.Files(f.Ledger.Directory));
    }

    [Fact]
    public async Task TelemetryFailureCannotDecideReadinessOrAlterState()
    {
        using var f = new FindingsMcpFixture(); await f.OpenAsync();
        await File.WriteAllTextAsync(f.Configuration.DiagnosticsDirectory, "not a directory");
        f.Configuration = f.Configuration with { AllowInspect = true };
        var before = await InspectionTests.Files(f.Ledger.Directory);
        var version = (await f.Ledger.StateAsync()).Version;
        var request = JsonSerializer.Serialize(new { schema_version = 1, action = "record_findings", expected_version = version,
            proposal = JsonDocument.Parse(FindingsMcpFixture.Body()).RootElement });
        var result = Body(Assert.Single(await f.ExchangeAsync(Call("check_readiness", request) + "\n")));
        Assert.Equal("ready", result.GetProperty("status").GetString());
        Assert.Equal(before, await InspectionTests.Files(f.Ledger.Directory));
    }
}
