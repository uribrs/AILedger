using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Tests.Findings;

namespace AILedger.Tests.ClaimDispositions;

[Collection(ClaimDispositionsRecordingCollection.Name)]
public sealed class ClaimDispositionsMcpTests
{
    public static IEnumerable<object[]> InvalidBodies()
    {
        var body = ClaimDispositionsMcpFixture.Body(ClaimDispositionsFixture.Single());
        foreach (var member in new[] { "actor_id", "task_id", "run_id", "correlation_id", "causation_id",
                     "allow_record_claim_dispositions", "capabilities", "allow_runless", "commands" })
            yield return [body.Insert(1, $"\"{member}\":\"spoofed\",")];
        yield return [body.Insert(1, "\"request_id\":\"duplicate\",")];
        yield return [body.Replace("\"key\":\"j1\"", "\"key\":\"j1\",\"key\":\"j2\"")];
        yield return [body.Replace("\"evidence_id\":\"E1\"", "\"claim_id\":\"E1\"")];
        yield return [body.Replace("\"claim_id\":\"C1\"", "\"finding\":\"C1\"")];
        yield return [body.Replace("\"status\":\"validated\"", "\"status\":1")];
        yield return [body.Replace("\"status\":\"validated\"", "\"status\":\"superseded\"")];
        yield return [body.Replace("\"schema_version\":1", "\"schema_version\":1.00000000000000000001")];
        foreach (var value in new[] { "null", "[]", "{}", "{\"schema_version\":1,\"request_id\":\"x\",\"dispositions\":[]}" }) yield return [value];
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task InvalidInputNeverEntersApplication(string body)
    {
        using var f = new ClaimDispositionsMcpFixture();
        await f.OpenAsync();
        var before = await File.ReadAllBytesAsync(f.Ledger.EventsPath);
        var response = await f.RecordAsync(body);
        Assert.Equal("invalid_request", response.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("not_committed", response.GetProperty("error").GetProperty("commit_state").GetString());
        Assert.Equal(before, await File.ReadAllBytesAsync(f.Ledger.EventsPath));
        Assert.False(Assert.Single(await f.AttemptsAsync()).GetProperty("application_entered").GetBoolean());
    }

    [Fact]
    public async Task ListsOnlyGrantedToolsAndEquivalentJsonRecoversAfterProcessRestart()
    {
        using var f = new ClaimDispositionsMcpFixture();
        await f.OpenAsync();
        const string listCall = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}\n";
        var list = Assert.Single(await f.ExchangeAsync(listCall));
        Assert.Equal(new[] { "record_findings", "record_claim_dispositions" },
            list.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        JsonElement receipt;
        using (var process = await f.StartAsync())
        {
            await process.SendAsync(ClaimDispositionsMcpFixture.Call(ClaimDispositionsMcpFixture.Body()));
            receipt = ClaimDispositionsMcpFixture.Payload(await process.ReadAsync()).GetProperty("receipt");
            Assert.Empty(await process.FinishAsync());
        }
        using (var process = await f.StartAsync())
        {
            var body = JsonNode.Parse(ClaimDispositionsMcpFixture.Body())!.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            body = body.Replace("\"schema_version\":1", "\"schema_version\":1e0");
            await process.SendAsync(ClaimDispositionsMcpFixture.Call(body));
            var retry = ClaimDispositionsMcpFixture.Payload(await process.ReadAsync());
            Assert.True(retry.GetProperty("replayed").GetBoolean());
            Assert.Equal(receipt.GetRawText(), retry.GetProperty("receipt").GetRawText());
            Assert.Empty(await process.FinishAsync());
        }
        f.Configuration = f.Configuration with { AllowRecordClaimDispositions = false };
        list = Assert.Single(await f.ExchangeAsync(listCall));
        Assert.Single(list.GetProperty("result").GetProperty("tools").EnumerateArray());
        Assert.Equal("authorization_denied", (await f.RecordAsync()).GetProperty("error").GetProperty("code").GetString());
    }
}
