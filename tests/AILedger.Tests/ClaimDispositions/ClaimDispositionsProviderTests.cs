using System.Text.Json;
using AILedger.Cli.Findings;
using AILedger.Core.Contracts;
using AILedger.Tests.Findings;

namespace AILedger.Tests.ClaimDispositions;

[Collection(ClaimDispositionsRecordingCollection.Name)]
public sealed class ClaimDispositionsProviderTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public async Task HostOwnedBindingSurvivesFileSpoofingReconnectAndPermissionRevocation(string provider)
    {
        using var f = new ClaimDispositionsMcpFixture();
        await f.OpenAsync();
        var subject = new ActorId("researcher");
        await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "assign", subject,
            RoleKind.PlanningLead, [Capability.ResolveClaim]));
        await f.Ledger.ExecuteAsync(new RequestStageTransitionCommand(f.Ledger.Actor, null, "research", TaskStage.Research,
            WithoutPrerequisitesReason: "Disposable provider binding test"));
        var start = await f.Ledger.ExecuteAsync(new StartRunCommand(f.Ledger.Actor, null, "dispatch", new("R1"), null,
            provider, null, SubjectActorId: subject));
        var cause = start.Events[^1].EventId;
        var configuration = f.Configuration with { ActorId = subject.Value, RunId = "R1", CorrelationId = "R1",
            CausationId = cause.Value, AllowRunless = false, Provider = provider };
        await using var host = new ProviderFindingsSession(configuration, f.Ledger.Service(), "unused", [],
            _ => Task.FromResult(configuration));
        // This used to be sufficient to rebind a file-host. The provider host never reads it.
        f.Configuration = f.Configuration with { ActorId = "operator", AllowRunless = true };
        await f.SaveConfigurationAsync();
        JsonElement receipt;
        using (var relay = new McpProcess(host.Endpoint.Arguments))
        {
            await InitializeAsync(relay);
            await relay.SendAsync(ClaimDispositionsMcpFixture.Call(ClaimDispositionsMcpFixture.Body()));
            var first = ClaimDispositionsMcpFixture.Payload(await relay.ReadAsync());
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
            await relay.SendAsync(ClaimDispositionsMcpFixture.Call(ClaimDispositionsMcpFixture.Body()));
            var retry = ClaimDispositionsMcpFixture.Payload(await relay.ReadAsync());
            Assert.True(retry.GetProperty("replayed").GetBoolean());
            Assert.Equal(receipt.GetRawText(), retry.GetProperty("receipt").GetRawText());
            configuration = configuration with { AllowRecordClaimDispositions = false };
            await relay.SendAsync(ClaimDispositionsMcpFixture.Call(ClaimDispositionsMcpFixture.Body(), 3));
            AssertError(await relay.ReadAsync(), "authorization_denied");
            configuration = configuration with { AllowRecordClaimDispositions = true };
            await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "revoke", subject,
                RoleKind.PlanningLead, [Capability.AddClaim]));
            await relay.SendAsync(ClaimDispositionsMcpFixture.Call(ClaimDispositionsMcpFixture.Body(), 4));
            AssertError(await relay.ReadAsync(), "kernel_refused");
            await relay.SendAsync(ClaimDispositionsMcpFixture.Call(ClaimDispositionsMcpFixture.Body().Replace("\"schema_version\":1",
                "\"schema_version\":1,\"actor_id\":\"operator\""), 5));
            AssertError(await relay.ReadAsync(), "invalid_request");
            Assert.Empty(await relay.FinishAsync());
        }
        Assert.Equal(2, (await f.Ledger.StateAsync()).Claims.Count);
        var after = await File.ReadAllBytesAsync(f.Ledger.EventsPath);
        Assert.True(after.AsSpan().StartsWith(before)); // Only the explicit role revocation was appended.
        var attempts = await f.AttemptsAsync();
        Assert.All(attempts, row =>
        {
            Assert.Equal(subject.Value, row.GetProperty("actor_id").GetString());
            Assert.Equal(provider, row.GetProperty("provider").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("provider_session_id").ValueKind);
        });
    }

    private static Task InitializeAsync(McpProcess relay) => ProviderFindingsSessionTests.InitializeAsync(relay);
    private static void AssertError(JsonElement rpc, string code)
    {
        var body = ClaimDispositionsMcpFixture.Payload(rpc);
        Assert.False(body.TryGetProperty("receipt", out _));
        Assert.Equal(code, body.GetProperty("error").GetProperty("code").GetString());
    }
}
