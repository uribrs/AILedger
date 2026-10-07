using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AILedger.Cli.Findings;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Findings;

public sealed class ProviderFindingsSessionTests
{
    [Fact]
    public async Task WritableConfigurationCannotExpandAnExistingHostGrant()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var original = f.Configuration with { AllowRecordFindings = false };
        var current = original;
        var host = new FindingsMcpHost(original, f.Ledger.Service(), _ => Task.FromResult(current));
        current = original with { AllowRecordFindings = true };
        await Assert.ThrowsAsync<InvalidDataException>(() => host.BindAsync(default));
        current = original with { ActorId = "another-operator" };
        await Assert.ThrowsAsync<InvalidDataException>(() => host.BindAsync(default));
    }

    [Fact]
    public async Task HostOwnedBindingSurvivesFileSpoofingReconnectAndPermissionRevocation()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var subject = new ActorId("researcher");
        await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "assign", subject,
            RoleKind.Researcher, [Capability.AddClaim, Capability.AddEvidence]));
        await f.Ledger.ExecuteAsync(new RequestStageTransitionCommand(f.Ledger.Actor, null, "research", TaskStage.Research,
            WithoutPrerequisitesReason: "Disposable provider binding test"));
        var start = await f.Ledger.ExecuteAsync(new StartRunCommand(f.Ledger.Actor, null, "dispatch", new("R1"), null,
            "codex", null, SubjectActorId: subject));
        var cause = start.Events[^1].EventId;
        var configuration = f.Configuration with { ActorId = subject.Value, RunId = "R1", CorrelationId = "R1",
            CausationId = cause.Value, AllowRunless = false, Provider = "codex" };
        await using var host = new ProviderFindingsSession(configuration, f.Ledger.Service(), "unused", [],
            _ => Task.FromResult(configuration));
        // This used to be sufficient to rebind a file-host. The provider host never reads it.
        f.Configuration = f.Configuration with { ActorId = "operator", AllowRunless = true };
        await f.SaveConfigurationAsync();
        JsonElement receipt;
        using (var relay = new McpProcess(host.Endpoint.Arguments))
        {
            await InitializeAsync(relay);
            await relay.SendAsync(FindingsMcpFixture.Call(FindingsMcpFixture.Body()));
            var first = FindingsMcpFixture.Payload(await relay.ReadAsync());
            receipt = first.GetProperty("receipt");
            Assert.Equal(subject.Value, receipt.GetProperty("actor_id").GetString());
            Assert.Equal("R1", receipt.GetProperty("run_id").GetString());
            Assert.Equal("R1", receipt.GetProperty("correlation_id").GetString());
            Assert.Equal(cause.Value, receipt.GetProperty("causation_id").GetString());
            Assert.Empty(await relay.FinishAsync());
        }
        var before = await File.ReadAllBytesAsync(f.Ledger.EventsPath);
        using (var relay = new McpProcess(host.Endpoint.Arguments))
        {
            await InitializeAsync(relay);
            await relay.SendAsync(FindingsMcpFixture.Call(FindingsMcpFixture.Body()));
            var retry = FindingsMcpFixture.Payload(await relay.ReadAsync());
            Assert.True(retry.GetProperty("replayed").GetBoolean());
            Assert.Equal(receipt.GetRawText(), retry.GetProperty("receipt").GetRawText());
            configuration = configuration with { AllowRecordFindings = false };
            await relay.SendAsync(FindingsMcpFixture.Call(FindingsMcpFixture.Body(), 3));
            AssertError(await relay.ReadAsync(), "authorization_denied");
            configuration = configuration with { AllowRecordFindings = true };
            await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "revoke", subject,
                RoleKind.Researcher, [Capability.AddClaim]));
            await relay.SendAsync(FindingsMcpFixture.Call(FindingsMcpFixture.Body(), 4));
            AssertError(await relay.ReadAsync(), "kernel_refused");
            await relay.SendAsync(FindingsMcpFixture.Call(FindingsMcpFixture.Body().Replace("\"schema_version\":1",
                "\"schema_version\":1,\"actor_id\":\"operator\""), 5));
            AssertError(await relay.ReadAsync(), "invalid_request");
            Assert.Empty(await relay.FinishAsync());
        }
        Assert.Single((await f.Ledger.StateAsync()).Claims);
        Assert.Single((await f.Ledger.StateAsync()).Evidence);
        var after = await File.ReadAllBytesAsync(f.Ledger.EventsPath);
        Assert.True(after.AsSpan().StartsWith(before)); // Only the explicit role revocation was appended.
        var attempts = await f.AttemptsAsync();
        Assert.All(attempts, row =>
        {
            Assert.Equal(subject.Value, row.GetProperty("actor_id").GetString());
            Assert.Equal("codex", row.GetProperty("provider").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("provider_session_id").ValueKind);
        });
    }

    [Fact]
    public async Task WrongRelayCapabilityCannotEnterMcpOrRecordAndSocketCannotBeRebound()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        await using var host = new ProviderFindingsSession(f.Configuration, f.Ledger.Service(), "unused", []);
        var port = int.Parse(host.Endpoint.Arguments[2]);
        using var impostor = new TcpListener(IPAddress.Loopback, port);
        Assert.Throws<SocketException>(() => impostor.Start());
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        await stream.WriteAsync(new byte[32]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(0, await stream.ReadAsync(new byte[1], timeout.Token));
        Assert.Empty((await f.Ledger.StateAsync()).Claims);
    }

    internal static async Task InitializeAsync(McpProcess relay)
    {
        await relay.SendAsync(FindingsMcpFixture.Initialize);
        Assert.True((await relay.ReadAsync()).TryGetProperty("result", out _));
        await relay.SendAsync(FindingsMcpFixture.Ready);
    }

    private static void AssertError(JsonElement rpc, string code)
    {
        var body = FindingsMcpFixture.Payload(rpc);
        Assert.False(body.TryGetProperty("receipt", out _));
        Assert.Equal(code, body.GetProperty("error").GetProperty("code").GetString());
    }
}
