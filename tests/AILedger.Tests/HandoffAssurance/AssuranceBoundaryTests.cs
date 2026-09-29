using System.Text.Json;
using AILedger.Cli.Findings;
using AILedger.Core.Assurance;
using AILedger.Core.Episodes;
using AILedger.Tests.Findings;
using AILedger.Tests.Inspection;

namespace AILedger.Tests.HandoffAssurance;

public sealed class AssuranceBoundaryTests
{
    [Theory]
    [InlineData("reviewer", 3)] [InlineData("verifier", 4)] [InlineData("operator", 2)]
    public async Task ActualStdioClientDiscoversScopedToolsCallsThemAndCannotSpoofIdentity(string principal, int toolCount)
    {
        using var f = await AssuranceFixture.CreateAsync();
        var path = Path.Combine(f.Root, "authority.json"); await File.WriteAllTextAsync(path, EpisodeExecutionValidation.Serialize(f.Policy));
        using var client = new McpProcess(["assurance", "serve", "--authority", path, "--store", Path.Combine(f.Root, "store"), "--principal", principal, "--session", principal + "-session"]);
        await ProviderFindingsSessionTests.InitializeAsync(client);
        await client.SendAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var tools = (await client.ReadAsync()).GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(toolCount, tools.Length);
        Assert.DoesNotContain(tools, t => t.GetProperty("name").GetString() == "record_findings");
        Assert.All(tools, t => Assert.False(t.GetProperty("inputSchema").GetProperty("additionalProperties").GetBoolean()));
        await client.SendAsync(InspectionMcpTests.Call("inspect_assurance", """{"schema_version":1,"area_id":"a"}""", 3));
        var inspection = InspectionMcpTests.Body(await client.ReadAsync()); Assert.Equal("ok", inspection.GetProperty("status").GetString());
        var binding = inspection.GetProperty("data").GetProperty("snapshot").GetProperty("binding_sha256").GetString();
        if (principal != "operator")
        {
            var body = EpisodeExecutionValidation.Serialize(new ReadAssuranceRequest(1, "actual-read", "a", binding!, ["a.cs"]));
            await client.SendAsync(InspectionMcpTests.Call("read_assurance", body, 4));
            var read = InspectionMcpTests.Body(await client.ReadAsync()); Assert.Equal(principal, read.GetProperty("receipt").GetProperty("principal").GetString());
            await client.SendAsync(InspectionMcpTests.Call("read_assurance", body, 5));
            Assert.True(InspectionMcpTests.Body(await client.ReadAsync()).GetProperty("replayed").GetBoolean());
        }
        await client.SendAsync(InspectionMcpTests.Call("inspect_assurance", """{"schema_version":1,"area_id":"a","principal":"operator"}""", 6));
        Assert.Equal("invalid_request", InspectionMcpTests.Body(await client.ReadAsync()).GetProperty("error").GetProperty("code").GetString());
        f.Policy = f.Policy with { Principals = f.Policy.Principals.Select(p => p.Id == principal ? p with { Enabled = false } : p).ToArray() };
        await File.WriteAllTextAsync(path, EpisodeExecutionValidation.Serialize(f.Policy));
        await client.SendAsync(InspectionMcpTests.Call("inspect_assurance", """{"schema_version":1,"area_id":"a"}""", 7));
        Assert.Equal("authorization_denied", InspectionMcpTests.Body(await client.ReadAsync()).GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(await client.FinishAsync());
    }

    [Theory]
    [InlineData("codex")] [InlineData("claude")]
    public async Task AuthenticatedProviderRelayUsesHostIdentityAndDoesNotAdvertiseAcceptanceToInspector(string provider)
    {
        using var f = await AssuranceFixture.CreateAsync();
        using var ledger = new FindingsMcpFixture(); await ledger.OpenAsync();
        var configuration = ledger.Configuration with { Provider = provider };
        await using var host = new ProviderFindingsSession(configuration, ledger.Ledger.Service(), "unused", [], assurance: f.Sessions["reviewer"]);
        using var client = new McpProcess(host.Endpoint.Arguments);
        await ProviderFindingsSessionTests.InitializeAsync(client);
        await client.SendAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var tools = (await client.ReadAsync()).GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        Assert.Contains(tools, t => t.GetProperty("name").GetString() == "record_assurance");
        Assert.DoesNotContain(tools, t => t.GetProperty("name").GetString() == "accept_assurance");
        await client.SendAsync(InspectionMcpTests.Call("inspect_assurance", """{"schema_version":1,"area_id":"a"}""", 3));
        Assert.Equal("ok", InspectionMcpTests.Body(await client.ReadAsync()).GetProperty("status").GetString());
        await client.SendAsync(InspectionMcpTests.Call("accept_assurance", "{}", 4));
        Assert.Equal("authorization_denied", InspectionMcpTests.Body(await client.ReadAsync()).GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(await client.FinishAsync()); Assert.Empty((await ledger.Ledger.StateAsync()).Claims);
    }

    [Fact]
    public async Task MissingBindingAndUninspectedPathsCannotBePresentedAsCompleteAssurance()
    {
        using var f = await AssuranceFixture.CreateAsync();
        using var body = JsonDocument.Parse("""{"schema_version":1,"request_id":"missing","area_id":"a","paths":["a.cs"]}""");
        Assert.Equal("invalid_request", (await f.Sessions["reviewer"].InvokeAsync("read_assurance", body.RootElement, default)).Error?.Code);
        var review = await f.ReportAsync("reviewer", status: "partial");
        var narrow = await f.CallAsync("reviewer", "record_assurance", review.Request with { RequestId = "narrow", Status = "complete", InspectedPaths = ["a.cs"], Supersedes = review.Response.Receipt!.Id });
        var verify = await f.ReportAsync("verifier");
        Assert.Equal("incomplete_assurance", (await f.AcceptAsync([narrow.Receipt!.Id, verify.Response.Receipt!.Id])).Error?.Code);
    }

    [Fact]
    public async Task ExpiredAuthorityAndUngrantableDependencyRemainUnavailable()
    {
        using var f = await AssuranceFixture.CreateAsync();
        f.Policy = f.Policy with { Principals = f.Policy.Principals.Select(p => p.Id == "reviewer" ? p with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) } : p).ToArray() };
        f.OpenSessions();
        Assert.Equal("authorization_denied", (await f.CallAsync("reviewer", "inspect_assurance", new InspectAssuranceRequest(1, "a"))).Error?.Code);
        f.Policy = f.Policy with { Principals = f.Policy.Principals.Select(p => p.Id == "reviewer" ? p with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1), Areas = ["b"] } : p).ToArray() };
        f.OpenSessions();
        Assert.Equal("authorization_denied", (await f.CallAsync("reviewer", "inspect_assurance", new InspectAssuranceRequest(1, "b"))).Error?.Code);
    }

    [Fact]
    public async Task ConcurrentIdenticalWritesAllocateOnlyOneReceiptAndChangedPolicyCannotReuseIt()
    {
        using var f = await AssuranceFixture.CreateAsync(); var binding = (await f.InspectAsync("reviewer")).Snapshot.BindingSha256;
        var request = new ReadAssuranceRequest(1, "concurrent", "a", binding, ["a.cs"]);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => f.CallAsync("reviewer", "read_assurance", request)));
        Assert.All(results, r => Assert.Null(r.Error)); Assert.Single(results.Where(r => !r.Replayed));
        Assert.Single(results.Select(r => r.Receipt!.Id).Distinct());
        f.Policy = f.Policy with { Implementer = "other-implementer" };
        Assert.Equal("authority_changed", (await f.CallAsync("reviewer", "read_assurance", request)).Error?.Code);
    }
}
