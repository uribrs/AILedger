using System.Text.Json;
using AILedger.Cli.Alternatives;
using AILedger.Core.Alternatives;
using AILedger.Tests.Findings;

namespace AILedger.Tests.Alternatives;

[Collection(AlternativesRecordingCollection.Name)]
public sealed class AlternativesMcpTests
{
    public static IEnumerable<object[]> InvalidBodies()
    {
        var body = AlternativesMcpFixture.Body(AlternativesFixture.Single());
        foreach (var member in new[] { "actor_id", "task_id", "run_id", "correlation_id", "causation_id",
                     "allow_record_alternatives", "allow_runless", "task_workspace_root", "Schema_version" })
            yield return [body.Insert(1, $"\"{member}\":\"spoofed\",")];
        yield return [body.Insert(1, "\"request_id\":\"duplicate\",")];
        yield return [body.Insert(1, "\"request_\\u0069d\":\"duplicate\",")];
        yield return [body.Replace("\"key\":\"a\"", "\"key\":\"a\",\"key\":\"b\"")];
        yield return [body.Replace("\"key\":\"a\"", "\"key\":null")];
        yield return [body.Replace("\"statement\":", "\"alternative_id\":\"A1\",\"statement\":")];
        yield return [body.Replace("\"schema_version\":1", "\"schema_version\":1.00000000000000000000001")];
        yield return [body.Replace("\"schema_version\":1", "\"schema_version\":\"1\"")];
        yield return [body.Replace("\"Approach\"", "\"\\uD800\"")];
        yield return [body.Replace("\"Approach\"", "null")];
        yield return ["""{"schema_version":1,"request_id":"x","alternatives":null}"""];
        yield return ["""{"schema_version":1,"request_id":"x","alternatives":[]}"""];
        yield return ["""{"schema_version":1,"request_id":"x"}"""];
        yield return ["null"];
        yield return ["[]"];
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task InvalidInputCannotEnterApplicationOrWrite(string body)
    {
        using var f = new AlternativesMcpFixture();
        await f.OpenAsync();
        var before = await File.ReadAllBytesAsync(f.Ledger.EventsPath);
        var result = await f.RecordAsync(body);
        Assert.Equal("invalid_request", result.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(before, await File.ReadAllBytesAsync(f.Ledger.EventsPath));
        Assert.False(Assert.Single(await f.AttemptsAsync()).GetProperty("application_entered").GetBoolean());
    }

    [Fact]
    public async Task ListsBothExactSchemasAndReturnsStableReceiptForEquivalentJsonAndRestart()
    {
        using var f = new AlternativesMcpFixture();
        await f.OpenAsync();
        var list = Assert.Single(await f.ExchangeAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""" + "\n"));
        var tools = list.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(new[] { "record_findings", "record_alternatives" }, tools.Select(t => t.GetProperty("name").GetString()));
        var body = AlternativesMcpFixture.Body(AlternativesFixture.Single());
        JsonElement receipt;
        using (var process = await f.StartAsync())
        {
            await process.SendAsync(AlternativesMcpFixture.Call(body));
            receipt = AlternativesMcpFixture.Payload(await process.ReadAsync()).GetProperty("receipt");
            Assert.Empty(await process.FinishAsync());
        }
        using (var process = await f.StartAsync())
        {
            var equivalent = """{"request_id":"single-1","alternatives":[{"from_lesson":null,"replaced_by_decision_id":null,"rejection_rationale":"Not suitable","statement":"\u0041pproach","key":"a"}],"schema_version":1e0}""";
            await process.SendAsync(AlternativesMcpFixture.Call(equivalent));
            var replay = AlternativesMcpFixture.Payload(await process.ReadAsync());
            Assert.True(replay.GetProperty("replayed").GetBoolean());
            Assert.Equal(receipt.GetRawText(), replay.GetProperty("receipt").GetRawText());
            Assert.Empty(await process.FinishAsync());
        }
        Assert.Single((await f.Ledger.StateAsync()).Alternatives);
    }

    [Fact]
    public async Task BoundsAndRevokedHostGrantsRemainUsefulErrors()
    {
        using var f = new AlternativesMcpFixture();
        await f.OpenAsync();
        var body = AlternativesMcpFixture.Body();
        var oversized = body.Insert(1, new string(' ', 256 * 1024));
        Assert.Equal("invalid_request", (await f.RecordAsync(oversized)).GetProperty("error").GetProperty("code").GetString());
        var original = await f.RecordAsync();
        Assert.Equal("committed", original.GetProperty("status").GetString());
        f.Configuration = f.Configuration with { AllowRecordAlternatives = false };
        var denied = await f.RecordAsync();
        Assert.Equal("authorization_denied", denied.GetProperty("error").GetProperty("code").GetString());
        Assert.False(denied.TryGetProperty("receipt", out _));
        Assert.Equal(2, (await f.Ledger.StateAsync()).Alternatives.Count);
    }
}
