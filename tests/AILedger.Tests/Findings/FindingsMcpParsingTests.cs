using System.Text;
using System.Text.Json;
using AILedger.Cli.Findings;
using AILedger.Core.Findings;

namespace AILedger.Tests.Findings;

public sealed class FindingsMcpParsingTests
{
    public static IEnumerable<object[]> InvalidBodies()
    {
        var body = FindingsMcpFixture.Body();
        foreach (var member in new[] { "task_id", "actor_id", "run_id", "correlation_id", "causation_id",
                     "allow_record_findings", "allow_runless", "task_workspace_root", "Schema_version" })
            yield return [body.Insert(1, $"\"{member}\":\"spoofed\",")];
        yield return [body.Insert(1, "\"request_id\":\"first\",")];
        yield return [body.Insert(1, "\"request_\\u0069d\":\"first\",")];
        yield return [body.Replace("\"key\":\"f1\"", "\"key\":\"f1\",\"key\":\"last\"")];
        yield return [body.Replace("\"finding\":\"f1\"", "\"finding\":\"f1\",\"finding\":\"f1\"")];
        yield return [body.Replace("\"finding\":\"f1\"", "\"finding\":\"f1\",\"claim_id\":null")];
        yield return [body.Replace("\"finding\":\"f1\"", "\"finding\":\"f1\",\"status\":\"validated\"")];
        yield return [body.Replace("\"key\":\"f1\"", "\"key\":\"f1\",\"status\":\"validated\"")];
        yield return [body.Replace("\"source_type\":", "\"unknown\":1,\"source_type\":")];
        yield return [body.Replace("\"request_id\":\"request-1\"", "\"request_id\":null")];
        yield return [body.Replace("\"schema_version\":1", "\"schema_version\":1e999")];
        yield return [body.Replace("\"schema_version\":1", "\"schema_version\":\"1\"")];
        yield return [body.Replace("\"key\":\"f1\"", "\"key\":null")];
        yield return [body.Replace("\"finding\":\"f1\"", "\"finding\":null")];
        yield return ["""{"schema_version":1,"request_id":"x","findings":[{"key":"f","statement":"\uD800"}],"evidence":[]}"""];
        yield return ["""{"schema_version":1,"request_id":"x","findings":[{"key":"f","statement":"\uDC00"}],"evidence":[]}"""];
        yield return ["""{"schema_version":1,"request_id":"x","findings":[{"key":"f","statement":"\uD800x"}],"evidence":[]}"""];
        yield return ["""{"schema_version":1,"request_id":"x","findings":null,"evidence":[]}"""];
        yield return ["""{"schema_version":1,"request_id":"x","findings":[],"evidence":[]}"""];
        yield return ["""{"schema_version":1,"request_id":"x","findings":[]}"""];
        yield return [body.Replace("\"schema_version\":1", "\"schema_version\":1.00000000000000000000000000001")];
        yield return ["[]"];
        yield return ["null"];
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task InvalidWireShapeIsRejectedBeforeApplicationEntry(string body)
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var before = await File.ReadAllBytesAsync(f.Ledger.EventsPath);
        var result = await f.RecordAsync(body);
        Assert.Equal("invalid_request", result.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("not_committed", result.GetProperty("error").GetProperty("commit_state").GetString());
        Assert.Equal(before, await File.ReadAllBytesAsync(f.Ledger.EventsPath));
        var row = Assert.Single(await f.AttemptsAsync());
        Assert.False(row.GetProperty("application_entered").GetBoolean());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("application_attempt_id").ValueKind);
        Assert.False(File.Exists(Path.Combine(f.Ledger.Directory, "findings-attempts.jsonl")));
    }

    [Theory]
    [InlineData("consequence", "findings[0].consequence", "consequence_if_wrong")]
    [InlineData("duplicate-key", "evidence[0].key", "unique")]
    public async Task CorrectableInputReportsLocationAndCanBeResubmitted(string defect, string path, string message)
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var valid = FindingsMcpFixture.Body();
        var invalid = defect == "consequence"
            ? valid.Replace("\"statement\":", "\"consequence\":\"private-value\",\"statement\":")
            : valid.Replace("\"key\":\"e1\"", "\"key\":\"f1\"");
        var result = await f.RecordAsync(invalid);
        var error = result.GetProperty("error");
        Assert.Equal("not_committed", error.GetProperty("commit_state").GetString());
        Assert.Equal("after_correction", error.GetProperty("retry").GetString());
        Assert.Equal(path, error.GetProperty("item_path").GetString());
        Assert.Contains(message, error.GetProperty("message").GetString());
        Assert.DoesNotContain("private-value", error.GetRawText());
        Assert.Empty((await f.Ledger.StateAsync()).Claims);
        var corrected = await f.RecordAsync(valid);
        Assert.Equal("committed", corrected.GetProperty("status").GetString());
        Assert.Single((await f.Ledger.StateAsync()).Claims);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("duplicate-envelope")]
    [InlineData("not-json")]
    [InlineData("NaN")]
    [InlineData("trailing-comma")]
    [InlineData("oversize-frame")]
    [InlineData("unterminated")]
    public async Task ProtocolFailuresAreObservedAndCannotWrite(string mode)
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var call = FindingsMcpFixture.Call(FindingsMcpFixture.Body());
        var raw = mode switch
        {
            "truncated" => call[..^5] + "\n",
            "duplicate-envelope" => call.Insert(1, "\"id\":99,") + "\n",
            "NaN" => call.Replace("\"schema_version\":1", "\"schema_version\":NaN") + "\n",
            "trailing-comma" => call[..^1] + ",}\n",
            "oversize-frame" => new string(' ', FindingsFrameReader.MaximumFrameBytes + 1) + "\n",
            "unterminated" => call,
            _ => "garbage\n"
        };
        var reply = Assert.Single(await f.ExchangeAsync(raw));
        Assert.True(reply.TryGetProperty("error", out _));
        Assert.Empty((await f.Ledger.StateAsync()).Claims);
        var row = Assert.Single(await f.AttemptsAsync());
        Assert.False(row.GetProperty("application_entered").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("transport_failure").ValueKind);
    }

    [Fact]
    public async Task InvalidUtf8IsNotSilentlyReplaced()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var wire = Encoding.UTF8.GetBytes(FindingsMcpFixture.Initialize + "\n" + FindingsMcpFixture.Ready + "\n" +
            FindingsMcpFixture.Call(FindingsMcpFixture.Body(FindingsFixture.Claims())) + "\n");
        wire[Array.IndexOf(wire, (byte)'S')] = 0xff;
        using var input = new MemoryStream(wire);
        using var output = new MemoryStream();
        var host = new FindingsMcpHost(f.Configuration, f.Ledger.Service(), _ => Task.FromResult(f.Configuration));
        await new FindingsMcpServer(host, input, output, TextWriter.Null).RunAsync(default);
        Assert.Contains("Invalid JSON-RPC", Encoding.UTF8.GetString(output.ToArray()));
        Assert.Empty((await f.Ledger.StateAsync()).Claims);
        Assert.False(Assert.Single(await f.AttemptsAsync()).GetProperty("application_entered").GetBoolean());
    }

    [Theory]
    [InlineData("wire-bytes")]
    [InlineData("findings")]
    [InlineData("evidence")]
    [InlineData("direction")]
    [InlineData("total-references")]
    [InlineData("statement")]
    [InlineData("normalized-bytes")]
    public async Task LimitsAreAppliedBeforeApplicationEntry(string limit)
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var request = FindingsFixture.Request();
        request = limit switch
        {
            "findings" => request with { Findings = Enumerable.Range(0, 33).Select(i => new FindingInput("f" + i, "text")).ToArray() },
            "evidence" => request with { Evidence = Enumerable.Range(0, 65).Select(i => request.Evidence[0] with { Key = "e" + i }).ToArray() },
            "direction" => request with { Evidence = [request.Evidence[0] with { Supports = Enumerable.Repeat(new FindingReference(Finding: "f1"), 65).ToArray() }] },
            "total-references" => request with { Evidence = Enumerable.Range(0, 17).Select(i => request.Evidence[0] with
                { Key = "e" + i, Supports = Enumerable.Range(0, 64).Select(n => new FindingReference(ClaimId: "C" + n)).ToArray() }).ToArray() },
            "statement" => request with { Findings = [new("f1", new string('a', 8193))] },
            // Omitted optionals fit on the wire; the canonical writer adds explicit null fields.
            "normalized-bytes" => new(1, "large", Enumerable.Range(0, 32).Select(i => new FindingInput("f" + i, new string('a', 8150))).ToArray(), []),
            _ => request
        };
        var body = FindingsMcpFixture.Body(request);
        if (limit == "wire-bytes") body = body.Insert(1, new string(' ', FindingsValidation.MaximumBodyBytes));
        if (limit == "normalized-bytes") body = body.Replace(",\"consequence_if_wrong\":null,\"from_lesson\":null", "");
        var result = await f.RecordAsync(body);
        Assert.Equal("invalid_request", result.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty((await f.Ledger.StateAsync()).Claims);
        Assert.False(Assert.Single(await f.AttemptsAsync()).GetProperty("application_entered").GetBoolean());
    }
}
