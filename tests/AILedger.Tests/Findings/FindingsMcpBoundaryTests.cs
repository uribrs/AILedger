using System.Text;
using System.Text.Json;
using AILedger.Cli.Findings;
using AILedger.Storage;
using AILedger.Core.Contracts;
using AILedger.Core.Findings;
using AILedger.Storage.Findings;

namespace AILedger.Tests.Findings;

public sealed class FindingsMcpBoundaryTests
{
    [Theory]
    [InlineData("grant")]
    [InlineData("runless")]
    [InlineData("missing-run")]
    [InlineData("run-correlation")]
    public async Task HostMustSupplyValidExplicitAuthority(string mode)
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        f.Configuration = mode switch
        {
            "grant" => f.Configuration with { AllowRecordFindings = false },
            "runless" => f.Configuration with { AllowRunless = false },
            "missing-run" => f.Configuration with { RunId = "R", CorrelationId = "R" },
            _ => f.Configuration with { RunId = "R", CorrelationId = "other" }
        };
        var result = await f.RecordAsync();
        Assert.Equal("authorization_denied", result.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty((await f.Ledger.StateAsync()).Claims);
        Assert.Equal("authorization_denied", Assert.Single(await f.AttemptsAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task PayloadAndClientMetadataCannotChangeHostAttribution()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var call = FindingsMcpFixture.Call(FindingsMcpFixture.Body());
        call = call.Replace("\"name\":\"record_findings\",", "\"name\":\"record_findings\",\"_meta\":{\"actor_id\":\"impostor\",\"allow_record_findings\":true},");
        var result = FindingsMcpFixture.Payload(Assert.Single(await f.ExchangeAsync(call + "\n")));
        Assert.Equal("operator", result.GetProperty("receipt").GetProperty("actor_id").GetString());
        Assert.Equal("stable", result.GetProperty("receipt").GetProperty("correlation_id").GetString());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1e0")]
    [InlineData("0.1e1")]
    [InlineData("100e-2")]
    public async Task NumericallyExactV1VersionsHaveEquivalentMeaning(string number)
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var result = await f.RecordAsync(FindingsMcpFixture.Body().Replace("\"schema_version\":1", "\"schema_version\":" + number));
        Assert.Equal("committed", result.GetProperty("status").GetString());
        Assert.True((await f.RecordAsync()).GetProperty("replayed").GetBoolean());
    }

    [Fact]
    public async Task ExactWireLimitIsAcceptedAndOversizedFrameDoesNotPoisonNextMessage()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var body = FindingsMcpFixture.Body();
        var padded = body.Insert(1, new string(' ', FindingsValidation.MaximumBodyBytes - Encoding.UTF8.GetByteCount(body)));
        Assert.Equal(FindingsValidation.MaximumBodyBytes, Encoding.UTF8.GetByteCount(padded));
        var messages = new string(' ', 300 * 1024) + "\n" + FindingsMcpFixture.Call(padded) + "\n";
        var replies = await f.ExchangeAsync(messages);
        Assert.Equal(2, replies.Length);
        Assert.True(replies[0].TryGetProperty("error", out _));
        Assert.Equal("committed", FindingsMcpFixture.Payload(replies[1]).GetProperty("status").GetString());
        Assert.Equal(2, (await f.AttemptsAsync()).Length);
    }

    [Fact]
    public async Task ProtocolCannotInvokeToolsWithoutHandshakeOrAsNotification()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        await f.SaveConfigurationAsync();
        using var configured = new McpProcess(f.ConfigurationPath);
        await configured.SendAsync(FindingsMcpFixture.Call(FindingsMcpFixture.Body()));
        Assert.Equal(-32000, (await configured.ReadAsync()).GetProperty("error").GetProperty("code").GetInt32());
        await configured.SendAsync(FindingsMcpFixture.Initialize);
        await configured.ReadAsync();
        await configured.SendAsync(FindingsMcpFixture.Ready);
        await configured.SendAsync(FindingsMcpFixture.Call(FindingsMcpFixture.Body()).Replace("\"id\":2,", ""));
        await configured.SendAsync("""{"jsonrpc":"2.0","id":9,"method":"ping"}""");
        Assert.Equal(9, (await configured.ReadAsync()).GetProperty("id").GetInt32());
        Assert.Empty(await configured.FinishAsync());
        Assert.Empty((await f.Ledger.StateAsync()).Claims);
    }

    [Theory]
    [InlineData("missing-task", "task_not_found")]
    [InlineData("capacity", "capacity_exceeded")]
    [InlineData("reference", "invalid_reference")]
    [InlineData("history", "history_corrupt")]
    [InlineData("pre-append", "storage_unavailable")]
    public async Task ServiceOutcomesRetainFrozenErrorShape(string mode, string code)
    {
        using var f = new FindingsMcpFixture();
        if (mode != "missing-task") await f.OpenAsync();
        var recorder = mode == "capacity" ? f.Ledger.Service(cap: 2) : mode == "pre-append"
            ? f.Ledger.Faulted(new FindingsStorageFaults(BeforeWrite: () => throw new IOException("Before append")))
            : f.Ledger.Service();
        if (mode == "history")
            await File.AppendAllTextAsync(f.Ledger.EventsPath, "{invalid history}\n");
        var body = FindingsMcpFixture.Body();
        if (mode == "reference") body = body.Replace("\"finding\":\"f1\"", "\"finding\":\"missing\"");
        var result = await f.RecordAsync(body, recorder);
        Assert.Equal(code, result.GetProperty("error").GetProperty("code").GetString());
        Assert.False(result.TryGetProperty("receipt", out _));
        Assert.Equal(code, Assert.Single(await f.AttemptsAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task BoundConcurrencyRejectsExcessCallsAndKeepsCancellationResponsive()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        using var process = await f.StartAsync();
        await using (await new TaskMutationLock().AcquireAsync(Path.Combine(f.Ledger.Directory, ".mutation.lock"), default))
        {
            for (var id = 2; id <= 10; id++)
                await process.SendAsync(FindingsMcpFixture.Call(FindingsMcpFixture.Body(), id));
            var excess = await process.ReadAsync();
            Assert.Equal(10, excess.GetProperty("id").GetInt32());
            Assert.Equal(-32000, excess.GetProperty("error").GetProperty("code").GetInt32());
            for (var id = 2; id <= 9; id++)
                await process.SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":" + id + "}}");
            for (var id = 2; id <= 9; id++)
                Assert.Equal("storage_unavailable", FindingsMcpFixture.Payload(await process.ReadAsync())
                    .GetProperty("error").GetProperty("code").GetString());
        }
        Assert.Empty(await process.FinishAsync());
        Assert.Empty((await f.Ledger.StateAsync()).Claims);
        Assert.Equal(9, (await f.AttemptsAsync()).Length);
    }

    [Fact]
    public async Task InputFailureHasAnObservationWithoutInventedApplicationTiming()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        using var input = new FailedInput();
        using var output = new MemoryStream();
        var host = new FindingsMcpHost(f.Configuration, f.Ledger.Service(), _ => Task.FromResult(f.Configuration));
        await new FindingsMcpServer(host, input, output, TextWriter.Null).RunAsync(default);
        var row = Assert.Single(await f.AttemptsAsync());
        Assert.Equal("input_failed", row.GetProperty("transport_failure").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("application_ms").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("duration_ms").ValueKind);
        Assert.Empty(output.ToArray());
        Assert.Empty((await f.Ledger.StateAsync()).Claims);
    }

    private sealed class FailedInput : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Injected input failure"));
    }

    [Fact]
    public async Task OversizedAndDuplicateHostConfigurationFailBeforeProtocolStarts()
    {
        using var f = new FindingsMcpFixture();
        await File.WriteAllTextAsync(f.ConfigurationPath, new string('x', 16385));
        using (var process = new McpProcess(f.ConfigurationPath))
            Assert.Contains("startup_failed", await process.FinishAsync(1));
        await f.SaveConfigurationAsync();
        var configuration = await File.ReadAllTextAsync(f.ConfigurationPath);
        await File.WriteAllTextAsync(f.ConfigurationPath, configuration.Insert(1, "\"actor_id\":\"other\","));
        using (var process = new McpProcess(f.ConfigurationPath))
            Assert.Contains("startup_failed", await process.FinishAsync(1));
    }
}
